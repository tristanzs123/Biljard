using System;
using System.Collections;
using System.Collections.Generic;
using Billiards.AI;
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

        public GameState State { get; private set; } = GameState.MainMenu;
        public RulesEngine Rules { get; private set; }
        public AIDifficultySettings Difficulty { get; private set; }
        public IReadOnlyList<AIDifficultySettings> Difficulties => difficulties;
        public PlayerStats[] Stats { get; } = new PlayerStats[] { new PlayerStats(), new PlayerStats() };
        public bool BallInHandValid { get; private set; }
        public CueController Cue => cue;
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
            Time.fixedDeltaTime = fixedTimestep;
            Time.timeScale = 1f;
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
            State = GameState.MainMenu;
        }

        /// <summary>Starts a new game against the AI with the chosen difficulty index.</summary>
        public void StartGame(int difficultyIndex)
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
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Toggles the pause menu.</summary>
        public void TogglePause()
        {
            if (State == GameState.Paused)
            {
                State = stateBeforePause;
                Time.timeScale = 1f;
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

            switch (State)
            {
                case GameState.BallsMoving:
                    if (Time.time - shotStartTime > minShotTime && !Ball.AnyMoving() && !cue.IsShooting)
                        EvaluateShot();
                    break;
                case GameState.BallInHand:
                    UpdateHumanBallInHand();
                    break;
            }
        }

        // ------------------------------------------------------------------ turns

        void BeginTurn()
        {
            bool aiTurn = Rules.CurrentPlayer == 1;
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
                ai.TakeTurn(Rules, pockets, PlaceCueBall);
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

        void OnShotFired(Vector3 dir, float power)
        {
            if (Rules == null) return;
            shot.Clear();
            breakShot = Rules.IsBreak;
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
            switch (outcome.Type)
            {
                case ShotOutcomeType.Win:
                case ShotOutcomeType.Lose:
                    State = GameState.GameOver;
                    cue.HumanControl = false;
                    Message?.Invoke(Rules.Winner == 0 ? "ZMAGA!" : "PORAZ - zmagal je AI");
                    return;
                case ShotOutcomeType.Foul:
                    Stats[shooter].fouls++;
                    Message?.Invoke(outcome.Message);
                    break;
                case ShotOutcomeType.Continue:
                    Message?.Invoke(shooter == 0 ? "Zadel si - nadaljuj!" : "AI nadaljuje");
                    break;
                default:
                    Message?.Invoke(Rules.CurrentPlayer == 0 ? "Tvoja poteza" : "AI na potezi");
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

            if (mouse.leftButton.wasPressedThisFrame && BallInHandValid)
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
