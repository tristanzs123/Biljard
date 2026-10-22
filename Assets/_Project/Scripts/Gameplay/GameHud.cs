using Billiards.Rules;
using UnityEngine;

namespace Billiards.Gameplay
{
    /// <summary>
    /// Quick IMGUI HUD and menus: main menu with difficulty, turn info, groups, power bar, banners,
    /// pause and game over screens. (Can be replaced with uGUI/UI Toolkit later.)
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] GameManager game;

        [Tooltip("How long a banner message stays visible (s).")]
        [SerializeField, Range(0.5f, 5f)] float bannerTime = 2.2f;

        string banner = "";
        float bannerUntil;
        GUIStyle title, big, normal, bannerStyle, box;
        Texture2D white;

        void OnEnable() { if (game != null) game.Message += ShowBanner; }
        void OnDisable() { if (game != null) game.Message -= ShowBanner; }

        void ShowBanner(string text)
        {
            banner = text;
            bannerUntil = Time.unscaledTime + bannerTime;
        }

        void EnsureStyles()
        {
            if (title != null) return;
            white = Texture2D.whiteTexture;
            float s = Screen.height / 1080f;
            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(64 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            big = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(30 * s) };
            normal = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24 * s), richText = true };
            normal.normal.textColor = Color.white;
            bannerStyle = new GUIStyle(title) { fontSize = Mathf.RoundToInt(44 * s) };
            box = new GUIStyle(GUI.skin.box);
        }

        void OnGUI()
        {
            if (game == null) return;
            EnsureStyles();
            float s = Screen.height / 1080f;

            switch (game.State)
            {
                case GameState.MainMenu:
                    DrawMainMenu(s);
                    return;
                case GameState.Paused:
                    DrawHud(s);
                    DrawPause(s);
                    return;
                case GameState.GameOver:
                    DrawHud(s);
                    DrawGameOver(s);
                    return;
                default:
                    DrawHud(s);
                    break;
            }

            if (Time.unscaledTime < bannerUntil && banner.Length > 0)
            {
                float a = Mathf.Clamp01((bannerUntil - Time.unscaledTime) * 2f);
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                Shade(new Rect(0, Screen.height * 0.16f, Screen.width, 90 * s), 0.45f * a);
                GUI.Label(new Rect(0, Screen.height * 0.16f, Screen.width, 90 * s), banner, bannerStyle);
                GUI.color = c;
            }
        }

        void DrawMainMenu(float s)
        {
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.55f);
            GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 100 * s), "BILJARD 8-BALL", title);
            GUI.Label(new Rect(0, Screen.height * 0.12f + 90 * s, Screen.width, 50 * s),
                "<b>Igraj proti AI</b> - izberi težavnost", new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter });

            float w = 420 * s, h = 70 * s;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.38f;
            for (int i = 0; i < game.Difficulties.Count; i++)
            {
                string name = game.Difficulties[i] != null ? game.Difficulties[i].displayName : "AI " + (i + 1);
                if (GUI.Button(new Rect(x, y + i * (h + 14 * s), w, h), name, big)) game.StartGame(i);
            }

            float yi = y + game.Difficulties.Count * (h + 14 * s) + 20 * s;
            if (GUI.Button(new Rect(x, yi, w, h), "Izhod", big))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }

            GUI.Label(new Rect(x - 200 * s, yi + h + 30 * s, w + 400 * s, 160 * s),
                "Miška: meri  |  drži levi gumb in povleci nazaj: moč  |  spusti: udarec\n" +
                "Shift: natančno merjenje  |  desni gumb: prekliči  |  C: pogled  |  kolešček: zoom  |  Esc: pavza",
                new GUIStyle(normal) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.RoundToInt(20 * s) });
        }

        void DrawHud(float s)
        {
            RulesEngine rules = game.Rules;
            if (rules == null) return;

            Shade(new Rect(0, 0, Screen.width, 56 * s), 0.5f);
            bool myTurn = rules.CurrentPlayer == 0;
            string me = $"{(myTurn ? "» " : "")}<b>TI</b>  {GroupText(rules, 0)}";
            string ai = $"{GroupText(rules, 1)}  <b>AI ({game.Difficulty.displayName})</b>{(!myTurn ? " «" : "")}";
            GUI.Label(new Rect(20 * s, 10 * s, Screen.width * 0.45f, 40 * s), me, normal);
            GUI.Label(new Rect(Screen.width * 0.5f, 10 * s, Screen.width * 0.5f - 20 * s, 40 * s), ai,
                new GUIStyle(normal) { alignment = TextAnchor.UpperRight });

            string status = game.State switch
            {
                GameState.BallInHand => game.BallInHandValid ? "Bela v roki: klikni, kamor jo postaviš" : "Bela v roki: tukaj se prekriva",
                GameState.AITurn => game.AI.Status,
                GameState.BallsMoving => "",
                _ => myTurn && rules.IsBreak ? "Razbij trikotnik!" : ""
            };
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(0, 60 * s, Screen.width, 40 * s), status, new GUIStyle(normal) { alignment = TextAnchor.UpperCenter });

            // power bar
            CueController cue = game.Cue;
            if (cue != null && (cue.IsCharging || game.State == GameState.AITurn))
            {
                float p = cue.IsCharging ? cue.CurrentPower : 0f;
                Rect bar = new Rect(30 * s, Screen.height - 330 * s, 34 * s, 280 * s);
                Shade(bar, 0.6f);
                float fill = bar.height * p;
                Color c = GUI.color;
                GUI.color = Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(1f, 0.2f, 0.1f), p);
                GUI.DrawTexture(new Rect(bar.x + 4 * s, bar.yMax - fill, bar.width - 8 * s, fill), white);
                GUI.color = c;
                if (cue.IsCharging)
                    GUI.Label(new Rect(bar.x - 10 * s, bar.yMax + 4 * s, 120 * s, 30 * s), $"{Mathf.RoundToInt(p * 100)} %", normal);
            }
        }

        static string GroupText(RulesEngine rules, int player)
        {
            BallGroup g = rules.GetGroup(player);
            if (g == BallGroup.None) return "<color=#bbbbbb>(odprta miza)</color>";
            int left = rules.RemainingInGroup(player);
            string name = g == BallGroup.Solids ? "Polne" : "Črtaste";
            return left == 0 ? $"{name}: <color=#ffd54a>na vrsti je črna 8</color>" : $"{name}: {left}";
        }

        void DrawPause(float s)
        {
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.6f);
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 100 * s), "PAVZA", title);
            float w = 380 * s, h = 70 * s, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.4f;
            if (GUI.Button(new Rect(x, y, w, h), "Nadaljuj", big)) game.TogglePause();
            if (GUI.Button(new Rect(x, y + h + 14 * s, w, h), "Nova igra", big)) game.Restart();
        }

        void DrawGameOver(float s)
        {
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.6f);
            bool won = game.Rules.Winner == 0;
            GUI.Label(new Rect(0, Screen.height * 0.14f, Screen.width, 100 * s), won ? "ZMAGA!" : "PORAZ", title);

            float w = 700 * s, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.32f;
            Shade(new Rect(x, y, w, 200 * s), 0.5f);
            PlayerStats me = game.Stats[0], ai = game.Stats[1];
            GUI.Label(new Rect(x + 30 * s, y + 20 * s, w, 170 * s),
                "<b>Statistika</b>            TI        AI\n" +
                $"Streli                     {me.shots,4}      {ai.shots,4}\n" +
                $"Potopljene krogle   {me.pocketed,4}      {ai.pocketed,4}\n" +
                $"Natančnost             {me.Accuracy,3}%     {ai.Accuracy,3}%\n" +
                $"Prekrški                 {me.fouls,4}      {ai.fouls,4}", normal);

            float bw = 380 * s, bh = 70 * s;
            if (GUI.Button(new Rect((Screen.width - bw) * 0.5f, y + 230 * s, bw, bh), "Ponovno igraj", big)) game.Restart();
        }

        void Shade(Rect r, float alpha)
        {
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha * c.a);
            GUI.DrawTexture(r, white);
            GUI.color = c;
        }
    }
}
