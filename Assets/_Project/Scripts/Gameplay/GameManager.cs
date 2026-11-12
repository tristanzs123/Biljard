using System;
using System.Collections;
using System.Collections.Generic;
using Billiards.AI;
using Billiards.Core;
using Billiards.Rules;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Billiards.Gameplay
{
    /// <summary>Game states.</summary>
    public enum GameState { MainMenu, PlayerAiming, BallsMoving, EvaluatingShot, BallInHand, AITurn, Paused, GameOver }

    /// <summary>Per-player statistics.</summary>
    [Serializable]
    public class PlayerStats
    {
        public int shots;
        public int pocketed;
        public int fouls;

        /// <summary>Pocketed balls per shot in percent.</summary>
        public int Accuracy => shots == 0 ? 0 : Mathf.RoundToInt(100f * Mathf.Min(1f, pocketed / (float)shots));
    }

    /// <summary>Rewards of the last finished game.</summary>
    public struct GameReward
    {
        public bool won;
        public int coins;
        public int xp;
        public int levelsGained;
    }

    /// <summary>
    /// Game flow state machine: tracks a shot (first hit, cushions, pockets), asks the RulesEngine for the outcome
    /// and hands the turn to the player or the AI.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        /// <summary>Raised with a short message for the HUD banner.</summary>
        public event Action<string> Message;

        [Header("References")]
        [SerializeField] CueController cue;
        [SerializeField] AIPlayer ai;
        [SerializeField] CameraViews cameraViews;

        [Header("Difficulties (Lahka, Srednja, Težka, Profesionalna)")]
        [SerializeField] AIDifficultySettings[] difficulties = new AIDifficultySettings[0];

        [Header("Table")]
        [Tooltip("Playing surface size (m).")]
        [SerializeField] Vector2 tableSize = new Vector2(2.54f, 1.27f);

        [Tooltip("Physics step (s). 0.005 = 200 Hz: fast balls (7 m/s) move only 3.5 cm per step, so collisions stay precise.")]
        [SerializeField, Range(0.002f, 0.02f)] float fixedTimestep = 0.005f;

        [Tooltip("Shot is evaluated only after this minimum time (s).")]
        [SerializeField] float minShotTime = 0.3f;

        [Tooltip("Shot clock: seconds per turn, like in 8 Ball Pool. 0 = no limit.")]
        [SerializeField, Range(0f, 90f)] float turnTimeLimit = 30f;

        [Tooltip("Game speed (1 = normal, 2 = everything twice as fast). Can be changed during Play.")]
        [SerializeField, Range(0.25f, 4f)] float gameSpeed = 1f;

        [Tooltip("After this time (s) all balls are stopped and the shot is evaluated anyway.")]
        [SerializeField] float maxShotTime = 10f;

        public GameState State { get; private set; } = GameState.MainMenu;
        public RulesEngine Rules { get; private set; }
        public AIDifficultySettings Difficulty { get; private set; }
        public IReadOnlyList<AIDifficultySettings> Difficulties => difficulties;
        public PlayerStats[] Stats { get; } = new PlayerStats[] { new PlayerStats(), new PlayerStats() };
        public bool BallInHandValid { get; private set; }
        public CueController Cue => cue;
        public Progression Progress { get; private set; }
        public int CurrentStage { get; private set; } = -1;
        public GameReward LastReward { get; private set; }

        /// <summary>Seconds left on the shot clock.</summary>
        public float TurnTimeLeft { get; private set; }

        /// <summary>Shot clock length (s).</summary>
        public float TurnTimeLimit => turnTimeLimit;

        static int pendingStage = -1;
        static bool pendingTwoPlayers;

        /// <summary>Two players on the same computer (no AI).</summary>
        public bool TwoPlayers { get; private set; }

        /// <summary>Display name of a player.</summary>
        public string PlayerName(int player)
        {
            if (TwoPlayers) return player == 0 ? "IGRALEC 1" : "IGRALEC 2";
            return player == 0 ? "TI" : "AI";
        }

        /// <summary>Pocket the black 8 must go into this turn (index into Pockets), -1 = none.</summary>
        public int CalledPocket { get; private set; } = -1;

        // pocket of the last pocketed solid (1-7) and stripe (9-15): the 8 must follow the player's last ball
        int lastSolidPocket = -1, lastStripePocket = -1;

        /// <summary>Pocket where the player's last group ball dropped (-1 = unknown).</summary>
        public int RequiredPocket(int player)
        {
            if (Rules == null) return -1;
            BallGroup g = Rules.GetGroup(player);
            return g == BallGroup.Solids ? lastSolidPocket : g == BallGroup.Stripes ? lastStripePocket : -1;
        }

        /// <summary>Pocket centres (same order as the indices used for calling).</summary>
        public IReadOnlyList<Vector3> Pockets => pockets;

        /// <summary>Current player is on the black 8 (it must go into the pocket of his last ball).</summary>
        public bool NeedsPocketCall => Rules != null && State != GameState.GameOver && Rules.IsOnEight(Rules.CurrentPlayer);

        int NearestPocket(Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < pockets.Count; i++)
            {
                float d = (new Vector3(p.x - pockets[i].x, 0f, p.z - pockets[i].z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Is this player controlled by a human.</summary>
        public bool IsHuman(int player) => TwoPlayers || player == 0;
        float baseMaxSpeed;
        public AIPlayer AI => ai;

        readonly ShotResult shot = new ShotResult();
        readonly List<Vector3> pockets = new List<Vector3>(6);
        readonly List<Ball> balls = new List<Ball>(16);
        Ball cueBall;
        float shotStartTime;
        GameState stateBeforePause;
        bool breakShot;

        void Awake()
        {
            Progress = GetComponent<Progression>();
            if (Progress == null) Progress = gameObject.AddComponent<Progression>();
            Time.fixedDeltaTime = fixedTimestep;
            Time.timeScale = gameSpeed;
        }

        void OnEnable()
        {
            Ball.CueBallHit += OnCueBallHit;
            Ball.CushionHit += OnCushionHit;
            Ball.Pocketed += OnPocketed;
            Ball.LeftTable += OnLeftTable;
            if (cue != null) cue.ShotFired += OnShotFired;
        }

        void OnDisable()
        {
            Ball.CueBallHit -= OnCueBallHit;
            Ball.CushionHit -= OnCushionHit;
            Ball.Pocketed -= OnPocketed;
            Ball.LeftTable -= OnLeftTable;
            if (cue != null) cue.ShotFired -= OnShotFired;
        }

        void Start()
        {
            cueBall = cue.GetComponent<Ball>();
            foreach (Pocket p in FindObjectsByType<Pocket>(FindObjectsSortMode.None)) pockets.Add(p.transform.position);
            balls.AddRange(Ball.All);
            RandomizeRack();
            cue.HumanControl = false;
            baseMaxSpeed = cue.maxSpeed;
            State = GameState.MainMenu;
            ApplyCue();
            ApplyStageVisuals(Mathf.Max(0, pendingStage));

            if (pendingTwoPlayers)
            {
                pendingTwoPlayers = false;
                StartTwoPlayers();
            }
            else if (pendingStage >= 0)
            {
                int s = pendingStage;
                pendingStage = -1;
                if (!StartStage(s)) State = GameState.MainMenu;
            }
        }

        /// <summary>Pays the entry fee and starts a game at a stage. Returns false if locked or not enough coins.</summary>
        public bool StartStage(int stageIndex)
        {
            Stage stage = Progress.Stages[stageIndex];
            if (!Progress.IsUnlocked(stageIndex) || !Progress.TrySpend(stage.entryFee)) return false;
            CurrentStage = stageIndex;
            ApplyStageVisuals(stageIndex);
            ApplyCue();
            StartGame(stage.difficulty);
            return true;
        }

        /// <summary>Starts a game for two players on this computer (free, no rewards).</summary>
        public void StartTwoPlayers()
        {
            TwoPlayers = true;
            CurrentStage = -1;
            ApplyStageVisuals(0);
            ApplyCue();
            Difficulty = difficulties.Length > 0 ? difficulties[0] : ScriptableObject.CreateInstance<AIDifficultySettings>();
            Rules = new RulesEngine(0);
            Stats[0] = new PlayerStats();
            Stats[1] = new PlayerStats();
            Message?.Invoke("IGRALEC 1 razbija!");
            BeginTurn();
        }

        /// <summary>Plays the same stage again (reloads the scene).</summary>
        public void PlayAgain()
        {
            pendingTwoPlayers = TwoPlayers;
            pendingStage = TwoPlayers ? -1 : CurrentStage;
            Restart();
        }

        /// <summary>Applies the equipped cue (aim assist length, power and colour).</summary>
        public void ApplyCue()
        {
            CueItem item = Progress.Cues[Progress.EquippedCue];
            cue.assistLength = item.aimAssist;
            cue.maxSpeed = baseMaxSpeed * (1f + item.powerBonus / 100f);
            if (cue.stick != null)
            {
                Transform shaft = cue.stick.transform.Find("Les");
                if (shaft != null) Tint(shaft.GetComponent<Renderer>(), item.shaftColor);
            }
        }

        void ApplyStageVisuals(int stageIndex)
        {
            Color cloth = Progress.Stages[stageIndex].clothColor;
            GameObject table = GameObject.Find("BiljardnaMiza");
            if (table == null) return;
            foreach (Renderer r in table.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Sukno") Tint(r, cloth);
                else if (r.name.StartsWith("Blazina")) Tint(r, cloth * 0.85f);
            }
        }

        static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            r.SetPropertyBlock(block);
        }

        void GiveRewards()
        {
            if (TwoPlayers)
            {
                LastReward = new GameReward { won = true };
                return;
            }
            bool won = Rules.Winner == 0;
            Stage stage = CurrentStage >= 0 ? Progress.Stages[CurrentStage] : null;
            int coins = won && stage != null ? stage.reward : 0;
            int xp = (won ? (stage != null ? stage.winXp : 50) : 20) + 5 * Stats[0].pocketed;
            if (coins > 0) Progress.AddCoins(coins);
            int levels = Progress.AddXp(xp);
            if (!won) Progress.EnsureMinimumCoins();
            LastReward = new GameReward { won = won, coins = coins, xp = xp, levelsGained = levels };
        }

        /// <summary>Starts a new game against the AI with the chosen difficulty index.</summary>
        void StartGame(int difficultyIndex)
        {
            Difficulty = difficulties.Length > 0 ? difficulties[Mathf.Clamp(difficultyIndex, 0, difficulties.Length - 1)] : null;
            if (Difficulty == null) Difficulty = ScriptableObject.CreateInstance<AIDifficultySettings>();
            ai.Difficulty = Difficulty;
            Rules = new RulesEngine(0);
            Stats[0] = new PlayerStats();
            Stats[1] = new PlayerStats();
            Message?.Invoke("Tvoja poteza - razbij!");
            BeginTurn();
        }

        /// <summary>Reloads the scene (play again).</summary>
        public void Restart()
        {
            Time.timeScale = gameSpeed;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Toggles the pause menu.</summary>
        public void TogglePause()
        {
            if (State == GameState.Paused)
            {
                State = stateBeforePause;
                Time.timeScale = gameSpeed;
                if (State == GameState.PlayerAiming) cue.HumanControl = true;
            }
            else if (State != GameState.MainMenu && State != GameState.GameOver)
            {
                stateBeforePause = State;
                State = GameState.Paused;
                Time.timeScale = 0f;
                cue.HumanControl = false;
            }
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) TogglePause();

            UpdateShotClock();
            if (State != GameState.Paused && !Mathf.Approximately(Time.timeScale, gameSpeed)) Time.timeScale = gameSpeed;


            switch (State)
            {
                case GameState.BallsMoving:
                    if (Time.time - shotStartTime > minShotTime && !Ball.AnyMoving() && !cue.IsShooting)
                        EvaluateShot();
                    else if (Time.time - shotStartTime > maxShotTime)
                    {
                        // safety net: a ball jittering somewhere must never block the game
                        LogMovingBalls();
                        Ball.StopAll();
                        EvaluateShot();
                    }
                    break;
                case GameState.BallInHand:
                    UpdateHumanBallInHand();
                    break;
            }
        }

        // ------------------------------------------------------------------ turns

        void UpdateShotClock()
        {
            if (turnTimeLimit <= 0f || Rules == null) return;
            bool humanTurn = State == GameState.PlayerAiming || State == GameState.BallInHand;
            if ((humanTurn || State == GameState.AITurn) && !cue.IsShooting)
            {
                TurnTimeLeft = Mathf.Max(0f, TurnTimeLeft - Time.deltaTime);
                if (humanTurn && TurnTimeLeft <= 0f) OnTimeout();
            }
        }

        void OnTimeout()
        {
            cue.HumanControl = false;
            cue.HideAimVisuals();
            if (State == GameState.BallInHand)
            {
                Vector3 p = cueBall.transform.position;
                cueBall.PlaceAt(BallInHandValid ? p : cueBall.SpawnPosition);
            }
            Stats[Rules.CurrentPlayer].fouls++;
            ShotOutcome outcome = Rules.Timeout();
            Message?.Invoke("ČAS JE POTEKEL - prekršek!");
            BeginTurn();
        }

        void BeginTurn()
        {
            TurnTimeLeft = turnTimeLimit;
            CalledPocket = Rules.IsOnEight(Rules.CurrentPlayer) ? RequiredPocket(Rules.CurrentPlayer) : -1;
            bool aiTurn = !IsHuman(Rules.CurrentPlayer);
            if (Rules.BallInHand && !aiTurn)
            {
                State = GameState.BallInHand;
                cue.HumanControl = false;
                cue.HideAimVisuals();
                PrepareCueBallForHand();
                return;
            }

            if (aiTurn)
            {
                State = GameState.AITurn;
                cue.HumanControl = false;
                // on the black 8 the AI may only aim at the pocket of its last ball
                IReadOnlyList<Vector3> targets = CalledPocket >= 0 ? new List<Vector3> { pockets[CalledPocket] } : (IReadOnlyList<Vector3>)pockets;
                ai.TakeTurn(Rules, targets, PlaceCueBall);
            }
            else
            {
                State = GameState.PlayerAiming;
                StartCoroutine(EnableHumanNextFrame());
            }
        }

        IEnumerator EnableHumanNextFrame()
        {
            yield return null;
            if (State == GameState.PlayerAiming) cue.HumanControl = true;
        }

        static void LogMovingBalls()
        {
            foreach (Ball b in Ball.All)
                if (b.IsMoving)
                    Debug.LogWarning($"Krogla {b.name} se še premika: v={b.Body.linearVelocity.magnitude:0.000} w={b.Body.angularVelocity.magnitude:0.00} y={b.transform.position.y:0.000}");
        }

        void OnShotFired(Vector3 dir, float power)
        {
            if (Rules == null) return;
            shot.Clear();
            breakShot = Rules.IsBreak;
            shot.CalledPocket = CalledPocket;
            Stats[Rules.CurrentPlayer].shots++;
            shotStartTime = Time.time;
            cue.HumanControl = false;
            State = GameState.BallsMoving;
            if (breakShot && power > 0.7f && cameraViews != null) cameraViews.Shake(0.25f, 0.012f * power);
        }

        void EvaluateShot()
        {
            State = GameState.EvaluatingShot;
            int shooter = Rules.CurrentPlayer;
            foreach (int n in shot.Pocketed) if (n != 0) Stats[shooter].pocketed++;

            ShotOutcome outcome = Rules.Evaluate(shot);
            Debug.Log($"[Biljard] {PlayerName(shooter)}: prva zadeta={shot.FirstHitBall}, potopljene={shot.Pocketed.Count}, blazina po stiku={shot.CushionAfterContact} -> {outcome.Type} {outcome.Message}");
            switch (outcome.Type)
            {
                case ShotOutcomeType.Win:
                case ShotOutcomeType.Lose:
                    State = GameState.GameOver;
                    cue.HumanControl = false;
                    GiveRewards();
                    Message?.Invoke(TwoPlayers ? $"ZMAGAL JE {PlayerName(Rules.Winner)}!" : Rules.Winner == 0 ? "ZMAGA!" : "PORAZ - zmagal je AI");
                    return;
                case ShotOutcomeType.Foul:
                    Stats[shooter].fouls++;
                    Message?.Invoke(outcome.Message);
                    break;
                case ShotOutcomeType.Continue:
                    Message?.Invoke(TwoPlayers ? $"{PlayerName(shooter)} zadel - nadaljuje!" : shooter == 0 ? "Zadel si - nadaljuj!" : "AI nadaljuje");
                    break;
                default:
                    Message?.Invoke(TwoPlayers ? $"Na potezi: {PlayerName(Rules.CurrentPlayer)}" : Rules.CurrentPlayer == 0 ? "Tvoja poteza" : "AI na potezi");
                    break;
            }
            BeginTurn();
        }

        // ------------------------------------------------------------------ shot tracking

        void OnCueBallHit(Ball cueB, Ball other)
        {
            if (State != GameState.BallsMoving) return;
            if (shot.FirstHitBall < 0) shot.FirstHitBall = other.number;
        }

        void OnCushionHit(Ball b)
        {
            if (State != GameState.BallsMoving) return;
            if (!b.isCueBall) shot.BallsTouchingCushion.Add(b.number);
            if (shot.FirstHitBall >= 0) shot.CushionAfterContact = true;
        }

        void OnPocketed(Ball b)
        {
            if (State != GameState.BallsMoving) return;
            shot.Pocketed.Add(b.isCueBall ? 0 : b.number);
            if (!b.isCueBall)
            {
                int pocket = NearestPocket(b.transform.position);
                if (b.number == 8) shot.EightPocket = pocket;
                else if (b.number < 8) lastSolidPocket = pocket;
                else lastStripePocket = pocket;
            }
            if (b.isCueBall) shot.CueBallPocketed = true;
        }

        void OnLeftTable(Ball b)
        {
            if (State != GameState.BallsMoving) return;
            shot.OffTable.Add(b.isCueBall ? 0 : b.number);
        }

        // ------------------------------------------------------------------ ball in hand

        void PrepareCueBallForHand()
        {
            Vector3 start = cueBall.gameObject.activeSelf ? cueBall.Body.position : cueBall.SpawnPosition;
            cueBall.PlaceAt(start);
            cueBall.Body.isKinematic = true;
            cueBall.Collider.enabled = false;
        }

        void UpdateHumanBallInHand()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || cue.cam == null) return;

            Ray ray = cue.cam.ScreenPointToRay(mouse.position.ReadValue());
            Plane plane = new Plane(Vector3.up, new Vector3(0f, Ball.Radius, 0f));
            if (!plane.Raycast(ray, out float d)) return;

            Vector3 p = ray.GetPoint(d);
            float hx = tableSize.x * 0.5f - Ball.Radius - 0.005f;
            float hz = tableSize.y * 0.5f - Ball.Radius - 0.005f;
            float maxX = Rules.BallInHandKitchenOnly ? -tableSize.x * 0.25f : hx;
            p.x = Mathf.Clamp(p.x, -hx, maxX);
            p.z = Mathf.Clamp(p.z, -hz, hz);
            p.y = Ball.Radius;

            cueBall.transform.position = p;
            cueBall.Body.position = p;
            BallInHandValid = !Physics.CheckSphere(p + Vector3.up * 0.004f, Ball.Radius, ~0, QueryTriggerInteraction.Ignore);

            if (mouse.leftButton.wasPressedThisFrame && BallInHandValid && !CueController.IsOverUi(mouse.position.ReadValue()))
            {
                cueBall.PlaceAt(p);
                State = GameState.PlayerAiming;
                StartCoroutine(EnableHumanNextFrame());
            }
        }

        /// <summary>Used by the AI to place the cue ball.</summary>
        void PlaceCueBall(Vector3 position) => cueBall.PlaceAt(position);

        // ------------------------------------------------------------------ rack

        /// <summary>WPA rack: 8 in the middle, one solid and one stripe in the back corners, others random.</summary>
        void RandomizeRack()
        {
            var objectBalls = new List<Ball>(15);
            foreach (Ball b in balls) if (!b.isCueBall) objectBalls.Add(b);
            if (objectBalls.Count != 15) return;

            // slots sorted by row (distance from the apex) and then across the row
            var slots = new List<Vector3>(15);
            float minX = float.MaxValue;
            foreach (Ball b in objectBalls)
            {
                slots.Add(b.transform.position);
                minX = Mathf.Min(minX, b.transform.position.x);
            }
            float rowStep = Ball.Radius * 2f * 0.866f;
            float apexX = minX; // the rack points toward the head of the table (-X)
            slots.Sort((a, b) =>
            {
                int ra = Mathf.RoundToInt(Mathf.Abs(a.x - apexX) / rowStep);
                int rb = Mathf.RoundToInt(Mathf.Abs(b.x - apexX) / rowStep);
                return ra != rb ? ra.CompareTo(rb) : a.z.CompareTo(b.z);
            });
            // indices: row0 = 0, row1 = 1-2, row2 = 3-5 (4 = middle), row3 = 6-9, row4 = 10-14 (10 and 14 = corners)

            Ball Find(int n) { foreach (Ball b in objectBalls) if (b.number == n) return b; return null; }

            var solids = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
            var stripes = new List<int> { 9, 10, 11, 12, 13, 14, 15 };
            Shuffle(solids);
            Shuffle(stripes);

            var order = new int[15];
            order[4] = 8;
            bool solidLeft = UnityEngine.Random.value < 0.5f;
            order[10] = solidLeft ? solids[0] : stripes[0];
            order[14] = solidLeft ? stripes[0] : solids[0];
            var rest = new List<int>(12);
            for (int i = 1; i < 7; i++) { rest.Add(solids[i]); rest.Add(stripes[i]); }
            Shuffle(rest);
            int r = 0;
            for (int i = 0; i < 15; i++)
                if (i != 4 && i != 10 && i != 14) order[i] = rest[r++];

            for (int i = 0; i < 15; i++)
            {
                Ball b = Find(order[i]);
                if (b != null) b.PlaceAt(slots[i]);
            }
        }

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
