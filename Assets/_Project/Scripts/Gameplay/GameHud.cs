using Billiards.Core;
using Billiards.Rules;
using UnityEngine;

namespace Billiards.Gameplay
{
    /// <summary>
    /// IMGUI HUD and menus: main menu with locations (entry fee / reward / unlock level), cue shop, daily reward,
    /// coins + level bar, in-game HUD, pause and game over with rewards.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] GameManager game;

        [Tooltip("How long a banner message stays visible (s).")]
        [SerializeField, Range(0.5f, 5f)] float bannerTime = 2.2f;

        enum MenuTab { Play, Shop }

        string banner = "";
        float bannerUntil;
        MenuTab tab = MenuTab.Play;
        GUIStyle title, big, button, normal, small, center, bannerStyle, coinStyle;
        Texture2D white;
        float s;

        void OnEnable() { if (game != null) game.Message += ShowBanner; }
        void OnDisable() { if (game != null) game.Message -= ShowBanner; }

        void ShowBanner(string text)
        {
            banner = text;
            bannerUntil = Time.unscaledTime + bannerTime;
        }

        void EnsureStyles()
        {
            float scale = Screen.height / 1080f;
            if (title != null && Mathf.Approximately(scale, s)) return;
            s = scale;
            white = Texture2D.whiteTexture;
            title = new GUIStyle(GUI.skin.label) { fontSize = Px(70), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            title.normal.textColor = Color.white;
            big = new GUIStyle(GUI.skin.button) { fontSize = Px(30), fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = Px(24) };
            normal = new GUIStyle(GUI.skin.label) { fontSize = Px(24), richText = true, wordWrap = true };
            normal.normal.textColor = Color.white;
            small = new GUIStyle(normal) { fontSize = Px(19) };
            center = new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter };
            bannerStyle = new GUIStyle(title) { fontSize = Px(46) };
            coinStyle = new GUIStyle(normal) { fontSize = Px(28), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            coinStyle.normal.textColor = new Color(1f, 0.83f, 0.25f);
        }

        int Px(float v) => Mathf.Max(10, Mathf.RoundToInt(v * s));

        void OnGUI()
        {
            if (game == null || game.Progress == null) return;
            EnsureStyles();

            switch (game.State)
            {
                case GameState.MainMenu:
                    DrawMainMenu();
                    return;
                case GameState.Paused:
                    DrawHud();
                    DrawPause();
                    return;
                case GameState.GameOver:
                    DrawHud();
                    DrawGameOver();
                    return;
                default:
                    DrawHud();
                    break;
            }

            if (Time.unscaledTime < bannerUntil && banner.Length > 0)
            {
                float a = Mathf.Clamp01((bannerUntil - Time.unscaledTime) * 2f);
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                Rect r = new Rect(0, Screen.height * 0.16f, Screen.width, Px(90));
                Shade(r, 0.45f * a);
                GUI.Label(r, banner, bannerStyle);
                GUI.color = c;
            }
        }

        // ------------------------------------------------------------------ top bar (coins + level)

        void DrawProfileBar(float y)
        {
            Progression p = game.Progress;
            p.LevelProgress(out int cur, out int need);
            float w = Mathf.Max(Px(620), 300f);
            Rect r = new Rect(Screen.width - w - Px(24), y, w, Mathf.Max(Px(70), 52f));
            Shade(r, 0.5f);
            GUI.Label(new Rect(r.x + Px(14), r.y + Px(4), w * 0.5f, Px(34)), $"<b>Nivo {p.Level}</b>", normal);
            GUI.Label(new Rect(r.x, r.y + Px(4), w - Px(14), Px(34)), $"{p.Coins:N0} kovancev", coinStyle);
            Rect bar = new Rect(r.x + Px(14), r.y + Px(44), w - Px(28), Px(14));
            Fill(bar, new Color(0.15f, 0.15f, 0.15f, 0.9f));
            Fill(new Rect(bar.x, bar.y, bar.width * cur / Mathf.Max(1, need), bar.height), new Color(0.3f, 0.75f, 1f));
            GUI.Label(new Rect(bar.x, bar.y - Px(4), bar.width, Px(22)), $"{cur} / {need} XP", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = Px(14) });
        }

        // ------------------------------------------------------------------ main menu

        void DrawMainMenu()
        {
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.6f);
            DrawProfileBar(Px(20));
            GUI.Label(new Rect(0, Px(90), Screen.width, Px(100)), "BILJARD <color=#ffd54a>8-BALL</color>", title);

            float tw = Px(240), th = Px(56), tx = Screen.width * 0.5f - tw - Px(8), ty = Px(200);
            if (GUI.Toggle(new Rect(tx, ty, tw, th), tab == MenuTab.Play, "Igraj", big)) tab = MenuTab.Play;
            if (GUI.Toggle(new Rect(tx + tw + Px(16), ty, tw, th), tab == MenuTab.Shop, "Trgovina", big)) tab = MenuTab.Shop;

            if (tab == MenuTab.Play) DrawStages(ty + th + Px(30));
            else DrawShop(ty + th + Px(30));

            Progression p = game.Progress;
            float bw = Px(360), by = Screen.height - Px(150);
            GUI.enabled = p.DailyAvailable;
            if (GUI.Button(new Rect(Screen.width * 0.5f - bw - Px(10), by, bw, Px(60)),
                    p.DailyAvailable ? $"Dnevna nagrada: +{p.DailyRewardAmount}" : "Dnevna nagrada prevzeta", button))
            {
                p.CollectDaily();
                ShowBanner($"+{p.DailyRewardAmount} kovancev!");
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(Screen.width * 0.5f + Px(10), by, bw, Px(60)), "Izhod", button)) Quit();

            GUI.Label(new Rect(0, Screen.height - Px(70), Screen.width, Px(60)),
                "Miška: meri  |  drži levi gumb in povleci nazaj: moč  |  Shift: natančno  |  desni gumb: prekliči  |  C: pogled  |  kolešček: zoom  |  Esc: pavza",
                new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

            DrawMenuBanner();
        }

        void DrawStages(float y)
        {
            Progression p = game.Progress;
            int n = p.Stages.Length;
            float cw = Px(330), ch = Px(330), gap = Px(24);
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Stage st = p.Stages[i];
                Rect card = new Rect(x0 + i * (cw + gap), y, cw, ch);
                bool unlocked = p.IsUnlocked(i);
                Fill(card, unlocked ? new Color(st.clothColor.r * 0.6f, st.clothColor.g * 0.6f, st.clothColor.b * 0.6f, 0.95f) : new Color(0.15f, 0.15f, 0.15f, 0.95f));
                Fill(new Rect(card.x, card.y, card.width, Px(8)), unlocked ? new Color(1f, 0.83f, 0.25f) : Color.gray);

                GUI.Label(new Rect(card.x, card.y + Px(20), cw, Px(50)), $"<b>{st.name}</b>", new GUIStyle(center) { fontSize = Px(34) });
                string diff = game.Difficulties.Count > st.difficulty && game.Difficulties[st.difficulty] != null ? game.Difficulties[st.difficulty].displayName : "";
                GUI.Label(new Rect(card.x, card.y + Px(75), cw, Px(34)), $"AI: {diff}", center);
                GUI.Label(new Rect(card.x, card.y + Px(120), cw, Px(34)), st.entryFee == 0 ? "Vstopnina: <color=#7dff8a><b>BREZPLAČNO</b></color>" : $"Vstopnina: <b>{st.entryFee:N0}</b>", center);
                GUI.Label(new Rect(card.x, card.y + Px(155), cw, Px(34)), $"Nagrada: <color=#ffd54a><b>{st.reward:N0}</b></color>", center);
                GUI.Label(new Rect(card.x, card.y + Px(190), cw, Px(34)), $"+{st.winXp} XP za zmago", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

                Rect btn = new Rect(card.x + Px(30), card.yMax - Px(85), cw - Px(60), Px(60));
                if (!unlocked)
                {
                    GUI.enabled = false;
                    GUI.Button(btn, $"Odklene se na nivoju {st.requiredLevel}", button);
                    GUI.enabled = true;
                }
                else if (p.Coins < st.entryFee)
                {
                    GUI.enabled = false;
                    GUI.Button(btn, "Premalo kovancev", button);
                    GUI.enabled = true;
                }
                else if (GUI.Button(btn, "IGRAJ", big))
                {
                    game.StartStage(i);
                }
            }
        }

        void DrawShop(float y)
        {
            Progression p = game.Progress;
            int n = p.Cues.Length;
            float cw = Px(330), ch = Px(330), gap = Px(24);
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                CueItem cue = p.Cues[i];
                Rect card = new Rect(x0 + i * (cw + gap), y, cw, ch);
                bool owned = p.Owns(i);
                bool equipped = p.EquippedCue == i;
                Fill(card, new Color(0.12f, 0.12f, 0.14f, 0.95f));
                Fill(new Rect(card.x, card.y, card.width, Px(8)), equipped ? new Color(0.3f, 0.85f, 0.4f) : new Color(1f, 0.83f, 0.25f));

                GUI.Label(new Rect(card.x, card.y + Px(20), cw, Px(50)), $"<b>{cue.name}</b>", new GUIStyle(center) { fontSize = Px(34) });
                // cue preview
                Rect stick = new Rect(card.x + Px(30), card.y + Px(90), cw - Px(60), Px(12));
                Fill(new Rect(stick.x, stick.y, stick.width * 0.35f, stick.height), new Color(0.2f, 0.1f, 0.05f));
                Fill(new Rect(stick.x + stick.width * 0.35f, stick.y + Px(2), stick.width * 0.62f, stick.height - Px(4)), cue.shaftColor);
                Fill(new Rect(stick.xMax - stick.width * 0.03f, stick.y + Px(3), stick.width * 0.03f, stick.height - Px(6)), new Color(0.2f, 0.4f, 0.9f));

                GUI.Label(new Rect(card.x, card.y + Px(125), cw, Px(34)), $"Pomoč pri merjenju: <b>x{cue.aimAssist:0.0}</b>", center);
                GUI.Label(new Rect(card.x, card.y + Px(160), cw, Px(34)), $"Moč: <b>+{cue.powerBonus:0}%</b>", center);
                GUI.Label(new Rect(card.x, card.y + Px(195), cw, Px(34)), owned ? "V lasti" : $"Cena: <color=#ffd54a><b>{cue.price:N0}</b></color>", center);

                Rect btn = new Rect(card.x + Px(30), card.yMax - Px(85), cw - Px(60), Px(60));
                if (equipped)
                {
                    GUI.enabled = false;
                    GUI.Button(btn, "Opremljena", button);
                    GUI.enabled = true;
                }
                else if (owned)
                {
                    if (GUI.Button(btn, "Opremi", big)) { p.Equip(i); game.ApplyCue(); }
                }
                else
                {
                    GUI.enabled = p.Coins >= cue.price;
                    if (GUI.Button(btn, p.Coins >= cue.price ? "KUPI" : "Premalo kovancev", big))
                    {
                        if (p.Buy(i))
                        {
                            game.ApplyCue();
                            ShowBanner($"Kupil si palico {cue.name}!");
                        }
                    }
                    GUI.enabled = true;
                }
            }
        }

        void DrawMenuBanner()
        {
            if (Time.unscaledTime >= bannerUntil || banner.Length == 0) return;
            Rect r = new Rect(0, Screen.height * 0.5f - Px(45), Screen.width, Px(90));
            Shade(r, 0.6f);
            GUI.Label(r, banner, bannerStyle);
        }

        // ------------------------------------------------------------------ in-game HUD

        void DrawHud()
        {
            RulesEngine rules = game.Rules;
            if (rules == null) return;

            Shade(new Rect(0, 0, Screen.width, Px(56)), 0.55f);
            bool myTurn = rules.CurrentPlayer == 0;
            string me = $"{(myTurn ? "» " : "")}<b>TI</b>  {GroupText(rules, 0)}";
            string ai = $"{GroupText(rules, 1)}  <b>AI ({game.Difficulty.displayName})</b>{(!myTurn ? " «" : "")}";
            GUI.Label(new Rect(Px(20), Px(10), Screen.width * 0.4f, Px(40)), me, normal);
            GUI.Label(new Rect(Screen.width * 0.6f, Px(10), Screen.width * 0.4f - Px(20), Px(40)), ai, new GUIStyle(normal) { alignment = TextAnchor.UpperRight });
            if (game.CurrentStage >= 0)
            {
                Stage st = game.Progress.Stages[game.CurrentStage];
                GUI.Label(new Rect(0, Px(10), Screen.width, Px(40)), $"<b>{st.name}</b>   nagrada <color=#ffd54a>{st.reward:N0}</color>", center);
            }

            string status = game.State switch
            {
                GameState.BallInHand => game.BallInHandValid ? "Bela v roki: klikni, kamor jo postaviš" : "Bela v roki: tukaj se prekriva",
                GameState.AITurn => game.AI.Status,
                GameState.BallsMoving => "",
                _ => myTurn && rules.IsBreak ? "Razbij trikotnik!" : ""
            };
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(0, Px(62), Screen.width, Px(40)), status, center);

            CueController cue = game.Cue;
            if (cue != null && cue.IsCharging)
            {
                float p = cue.CurrentPower;
                Rect bar = new Rect(Px(30), Screen.height - Px(330), Px(34), Px(280));
                Shade(bar, 0.6f);
                float fill = bar.height * p;
                Fill(new Rect(bar.x + Px(4), bar.yMax - fill, bar.width - Px(8), fill),
                    Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(1f, 0.2f, 0.1f), p));
                GUI.Label(new Rect(bar.x - Px(10), bar.yMax + Px(4), Px(120), Px(30)), $"{Mathf.RoundToInt(p * 100)} %", normal);
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

        void DrawPause()
        {
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.6f);
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, Px(100)), "PAVZA", title);
            float w = Px(380), h = Px(70), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.4f;
            if (GUI.Button(new Rect(x, y, w, h), "Nadaljuj", big)) game.TogglePause();
            if (GUI.Button(new Rect(x, y + h + Px(14), w, h), "Predaj in v meni", big)) game.Restart();
        }

        void DrawGameOver()
        {
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.65f);
            GameReward rw = game.LastReward;
            GUI.Label(new Rect(0, Screen.height * 0.08f, Screen.width, Px(110)),
                rw.won ? "<color=#ffd54a>ZMAGA!</color>" : "PORAZ", new GUIStyle(title) { fontSize = Px(90) });

            float w = Px(760), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.24f;
            Shade(new Rect(x, y, w, Px(120)), 0.5f);
            GUI.Label(new Rect(x, y + Px(10), w, Px(50)), $"<color=#ffd54a><b>+{rw.coins:N0} kovancev</b></color>     <color=#6cc4ff><b>+{rw.xp} XP</b></color>",
                new GUIStyle(center) { fontSize = Px(36) });
            if (rw.levelsGained > 0)
                GUI.Label(new Rect(x, y + Px(62), w, Px(50)), $"<b>NOV NIVO {game.Progress.Level}!</b>  Odklepaš nove mize.", new GUIStyle(center) { fontSize = Px(30) });

            y += Px(140);
            Shade(new Rect(x, y, w, Px(200)), 0.5f);
            PlayerStats me = game.Stats[0], ai = game.Stats[1];
            GUI.Label(new Rect(x + Px(40), y + Px(16), w - Px(80), Px(180)),
                "<b>Statistika</b>\n" +
                $"Streli:  ti {me.shots}  |  AI {ai.shots}\n" +
                $"Potopljene krogle:  ti {me.pocketed}  |  AI {ai.pocketed}\n" +
                $"Natančnost:  ti {me.Accuracy}%  |  AI {ai.Accuracy}%\n" +
                $"Prekrški:  ti {me.fouls}  |  AI {ai.fouls}", normal);

            DrawProfileBar(y + Px(220));

            float bw = Px(360), bh = Px(70), by = y + Px(320);
            Stage st = game.CurrentStage >= 0 ? game.Progress.Stages[game.CurrentStage] : null;
            bool canAgain = st != null && game.Progress.Coins >= st.entryFee;
            GUI.enabled = canAgain;
            if (GUI.Button(new Rect(Screen.width * 0.5f - bw - Px(10), by, bw, bh), st != null && st.entryFee > 0 ? $"Igraj znova ({st.entryFee:N0})" : "Igraj znova", big)) game.PlayAgain();
            GUI.enabled = true;
            if (GUI.Button(new Rect(Screen.width * 0.5f + Px(10), by, bw, bh), "Glavni meni", big)) game.Restart();
        }

        // ------------------------------------------------------------------ helpers

        void Shade(Rect r, float alpha) => Fill(r, new Color(0f, 0f, 0f, alpha));

        void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
