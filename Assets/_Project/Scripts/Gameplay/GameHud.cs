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

        enum MenuTab { Play, Shop, Settings }

        string banner = "";
        float bannerUntil;
        MenuTab tab = MenuTab.Play;

        // intro: 0 = loading screen, 1 = controls, 2 = done (main menu)
        // static: the intro is shown only once per game start, not after every "Glavni meni" (scene reload)
        static int introPhase;
        static float introStart = -1f;
        const float LoadingTime = 3f;
        static readonly string[] LoadingTips =
        {
            "Nalagam mizo ...", "Postavljam krogle v trikotnik ...", "Mažem palico s kredo ...", "Ogrevam AI nasprotnika ..."
        };
        bool pauseSettings;
        GUIStyle title, big, button, normal, small, center, bannerStyle, coinStyle, ballNum;
        Texture2D white, disc;
        GUIStyle panelStyle, pillStyle, goldPill, greenPill, darkPill, tabPill;
        static Texture2D roundTex, pillTex, bgGradient, vignette;
        static Font uiFont;

        static readonly Color[] BallColors =
        {
            Color.white,
            new Color32(247, 198, 0, 255), new Color32(21, 68, 181, 255), new Color32(214, 40, 40, 255),
            new Color32(91, 44, 143, 255), new Color32(240, 127, 18, 255), new Color32(19, 122, 58, 255),
            new Color32(122, 31, 31, 255), new Color32(18, 18, 18, 255)
        };
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
            // modern Windows UI font (falls back to Arial)
            if (uiFont == null) uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 32);
            title = new GUIStyle(GUI.skin.label) { fontSize = FontPx(70), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            title.normal.textColor = Color.white;
            big = new GUIStyle(GUI.skin.button) { fontSize = FontPx(30), fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = FontPx(24) };
            normal = new GUIStyle(GUI.skin.label) { fontSize = FontPx(24), richText = true, wordWrap = true };
            normal.normal.textColor = Color.white;
            small = new GUIStyle(normal) { fontSize = FontPx(19) };
            center = new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter };
            bannerStyle = new GUIStyle(title) { fontSize = FontPx(46) };
            coinStyle = new GUIStyle(normal) { fontSize = FontPx(28), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            coinStyle.normal.textColor = new Color(1f, 0.83f, 0.25f);
            ballNum = new GUIStyle(GUI.skin.label) { fontSize = FontPx(15), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            ballNum.normal.textColor = Color.black;
            if (disc == null) disc = MakeDisc(64);
            foreach (GUIStyle st in new[] { title, normal, small, center, bannerStyle, coinStyle, ballNum }) st.font = uiFont;
            BuildMenuStyles();
            // the shared big / normal buttons (pause, game over, settings) get the new look too
            big = new GUIStyle(goldPill) { fontSize = FontPx(28) };
            button = new GUIStyle(darkPill) { fontSize = FontPx(20) };
        }

        /// <summary>Converts 1080p design pixels to screen pixels (no minimum, so layouts keep their proportions).</summary>
        int Px(float v) => Mathf.Max(1, Mathf.RoundToInt(v * s));

        /// <summary>Font size for 1080p design pixels (never unreadably small).</summary>
        int FontPx(float v) => Mathf.Max(9, Mathf.RoundToInt(v * s));

        void OnGUI()
        {
            if (game == null || game.Progress == null) return;
            GameSettings.EnsureLoaded();
            EnsureStyles();
            if (Event.current.type == EventType.Layout) CueController.UiBlockers.Clear();

            if (introPhase < 2 && game.State == GameState.MainMenu)
            {
                if (introStart < 0f) introStart = Time.unscaledTime;
                if (introPhase == 0) DrawLoading();
                else DrawControlsIntro();
                return;
            }

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

        // ------------------------------------------------------------------ main menu (8 Ball Pool style lobby)

        static readonly string[] DifficultyNames = { "LAHKA", "SREDNJA", "TEŽKA", "PROFI" };
        static readonly Color[] DifficultyColors =
        {
            new Color(0.3f, 0.8f, 0.45f), new Color(0.95f, 0.78f, 0.25f), new Color(0.98f, 0.52f, 0.2f), new Color(0.92f, 0.25f, 0.25f)
        };

        /// <summary>Top bar: avatar with level, XP bar, daily reward and coins.</summary>
        void DrawProfileBar(float y)
        {
            Progression p = game.Progress;
            p.LevelProgress(out int cur, out int need);

            Rect bar = new Rect(Px(20), y, Screen.width - Px(40), Px(84));
            Panel(bar, new Color(0.04f, 0.05f, 0.07f, 0.82f));
            Panel(new Rect(bar.x, bar.yMax - Px(3), bar.width, Px(3)), new Color(1f, 1f, 1f, 0.05f));

            // avatar: gold ring, felt-green face, level number
            Rect av = new Rect(bar.x + Px(14), bar.y + Px(10), Px(64), Px(64));
            DrawDisc(new Rect(av.x - Px(2), av.y - Px(2), av.width + Px(4), av.height + Px(4)), new Color(0f, 0f, 0f, 0.5f));
            DrawDisc(av, new Color(1f, 0.78f, 0.22f));
            DrawDisc(new Rect(av.x + Px(5), av.y + Px(5), av.width - Px(10), av.height - Px(10)), new Color(0.07f, 0.32f, 0.18f));
            GUI.Label(av, $"<b>{p.Level}</b>", Style(center, 30, Color.white));

            float nx = av.xMax + Px(16), nw = Px(300);
            GUI.Label(new Rect(nx, bar.y + Px(10), nw, Px(32)), "<b>IGRALEC</b>", Style(normal, 24, Color.white));
            GUI.Label(new Rect(nx, bar.y + Px(10), nw, Px(32)), $"<b>NIVO {p.Level}</b>", Style(normal, 17, Gold, TextAnchor.MiddleRight));
            Rect xp = new Rect(nx, bar.y + Px(48), nw, Px(14));
            Panel(xp, new Color(1f, 1f, 1f, 0.08f), true);
            float f = Mathf.Clamp01(cur / (float)Mathf.Max(1, need));
            if (f > 0.02f) Panel(new Rect(xp.x, xp.y, Mathf.Max(xp.height, xp.width * f), xp.height), new Color(0.35f, 0.75f, 1f), true);
            GUI.Label(new Rect(nx, bar.y + Px(60), nw, Px(22)), $"{cur} / {need} XP", Style(small, 14, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight));

            // coins
            Rect coins = new Rect(bar.xMax - Px(14) - Px(250), bar.y + Px(14), Px(250), Px(56));
            Panel(coins, new Color(0f, 0f, 0f, 0.45f), true);
            DrawCoin(new Rect(coins.x + Px(8), coins.y + Px(8), Px(40), Px(40)));
            GUI.Label(new Rect(coins.x, coins.y, coins.width - Px(20), coins.height), $"<b>{p.Coins:N0}</b>", Style(normal, 30, Color.white, TextAnchor.MiddleRight));

            // daily reward
            Rect daily = new Rect(coins.x - Px(16) - Px(290), coins.y, Px(290), coins.height);
            if (p.DailyAvailable)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                Panel(new Rect(daily.x - Px(4), daily.y - Px(4), daily.width + Px(8), daily.height + Px(8)), new Color(1f, 0.8f, 0.2f, 0.15f + 0.3f * pulse), true);
                if (GUI.Button(daily, $"DNEVNA NAGRADA  +{p.DailyRewardAmount}", goldPill))
                {
                    p.CollectDaily();
                    ShowBanner($"+{p.DailyRewardAmount} kovancev!");
                }
            }
            else
            {
                Panel(daily, new Color(1f, 1f, 1f, 0.05f), true);
                GUI.Label(daily, "Dnevna nagrada prevzeta", Style(small, 17, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter));
            }
        }

        void DrawMenuBackground()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), bgGradient);

            // lamp light over the "table"
            DrawDisc(new Rect(Screen.width * 0.5f - Px(900), -Px(700), Px(1800), Px(1300)), new Color(0.12f, 0.6f, 0.34f, 0.13f));
            DrawDisc(new Rect(Screen.width * 0.5f - Px(500), -Px(380), Px(1000), Px(700)), new Color(0.2f, 0.75f, 0.45f, 0.08f));

            // huge faint black 8 as decoration (bottom right)
            float t = Time.unscaledTime * 0.25f;
            DrawBigGhostBall(new Rect(Screen.width - Px(330), Screen.height - Px(380) + Mathf.Sin(t) * Px(10), Px(560), Px(560)), 8);

            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
        }

        void DrawBigGhostBall(Rect r, int n)
        {
            Color baseColor = BallColors[n <= 8 ? n : n - 8];
            if (n >= 9)
            {
                DrawDisc(r, new Color(1f, 1f, 1f, 0.05f));
                float inset = r.height * 0.22f;
                DrawDisc(new Rect(r.x + inset * 0.2f, r.y + inset, r.width - inset * 0.4f, r.height - 2f * inset), new Color(baseColor.r, baseColor.g, baseColor.b, 0.12f));
            }
            else DrawDisc(r, new Color(0f, 0f, 0f, 0.55f));
            float c = r.width * 0.42f;
            DrawDisc(new Rect(r.center.x - c * 0.5f, r.center.y - c * 0.5f, c, c), new Color(1f, 1f, 1f, 0.07f));
            GUI.Label(new Rect(r.center.x - c * 0.5f, r.center.y - c * 0.5f, c, c), n.ToString(), Style(center, Mathf.RoundToInt(c * 0.6f / Mathf.Max(0.01f, s)), new Color(1f, 1f, 1f, 0.1f)));
            DrawDisc(new Rect(r.x + r.width * 0.18f, r.y + r.height * 0.12f, r.width * 0.28f, r.height * 0.2f), new Color(1f, 1f, 1f, 0.05f));
        }

        void DrawMenuTitle(float y)
        {
            Rect r = new Rect(0, y, Screen.width, Px(120));
            GUIStyle t = Style(title, 112, Color.white);
            t.fontStyle = FontStyle.Bold;
            // soft shadow + outline
            GUIStyle sh = new GUIStyle(t);
            sh.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + Px(5), r.y + Px(7), r.width, r.height), "BILJARD", sh);
            sh.normal.textColor = new Color(0.02f, 0.12f, 0.07f, 0.9f);
            for (int i = 0; i < 4; i++)
            {
                float ox = (i % 2 == 0 ? -1 : 1) * Px(2), oy = (i < 2 ? -1 : 1) * Px(2);
                GUI.Label(new Rect(r.x + ox, r.y + oy, r.width, r.height), "BILJARD", sh);
            }
            GUI.Label(r, "BILJARD", t);

            // ribbon "8-BALL POOL" with a small 8-ball
            Rect ribbon = new Rect(Screen.width * 0.5f - Px(150), y + Px(114), Px(300), Px(44));
            Panel(new Rect(ribbon.x, ribbon.y + Px(4), ribbon.width, ribbon.height), new Color(0f, 0f, 0f, 0.4f), true);
            Panel(ribbon, Gold, true);
            float bs = Px(34);
            DrawBall(new Rect(ribbon.x + Px(6), ribbon.y + Px(5), bs, bs), 8, 1f);
            GUI.Label(new Rect(ribbon.x + Px(30), ribbon.y, ribbon.width - Px(30), ribbon.height), "<b>8-BALL POOL</b>", Style(center, 24, new Color(0.15f, 0.09f, 0.01f)));

            GUI.Label(new Rect(0, y + Px(164), Screen.width, Px(30)), "Igraj proti AI nasprotniku ali s prijateljem", Style(small, 18, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter));
        }

        void DrawTabs(float y)
        {
            string[] names = { "IGRAJ", "TRGOVINA", "NASTAVITVE" };
            float tw = Px(230), th = Px(54), pad = Px(6);
            Rect bar = new Rect(Screen.width * 0.5f - (tw * 3 + pad * 2) * 0.5f, y, tw * 3 + pad * 2, th + pad * 2);
            Panel(bar, new Color(0f, 0f, 0f, 0.5f), true);
            for (int i = 0; i < 3; i++)
            {
                Rect r = new Rect(bar.x + pad + i * tw, bar.y + pad, tw, th);
                bool on = (int)tab == i;
                if (GUI.Button(r, names[i], on ? goldPill : tabPill)) tab = (MenuTab)i;
            }
        }

        void DrawMainMenu()
        {
            DrawMenuBackground();
            DrawProfileBar(Px(16));
            DrawMenuTitle(Px(118));
            DrawTabs(Px(326));

            float contentY = Px(418);
            if (tab == MenuTab.Play) DrawStages(contentY);
            else if (tab == MenuTab.Shop) DrawShop(contentY);
            else DrawSettings(contentY);

            // bottom
            if (GUI.Button(new Rect(Px(24), Screen.height - Px(74), Px(150), Px(50)), "IZHOD", darkPill)) Quit();
            string[] hints = { "MIŠKA  meri", "LEVI GUMB + POTEG  moč", "SHIFT  natančno", "KROGLA DESNO SPODAJ  spin", "ESC  pavza" };
            GUIStyle hs = Style(small, 15, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
            float total = 0f;
            float[] widths = new float[hints.Length];
            for (int i = 0; i < hints.Length; i++) { widths[i] = hs.CalcSize(new GUIContent(hints[i])).x + Px(28); total += widths[i] + Px(10); }
            float hx = Screen.width * 0.5f - total * 0.5f;
            for (int i = 0; i < hints.Length; i++)
            {
                Rect hr = new Rect(hx, Screen.height - Px(66), widths[i], Px(34));
                Panel(hr, new Color(1f, 1f, 1f, 0.06f), true);
                GUI.Label(hr, hints[i], hs);
                hx += widths[i] + Px(10);
            }

            DrawMenuBanner();
        }

        /// <summary>Card illustration: a pool table seen from above in the stage cloth colour.</summary>
        void DrawMiniTable(Rect area, Color cloth, bool unlocked)
        {
            if (!unlocked) cloth = Color.Lerp(cloth, new Color(0.35f, 0.35f, 0.35f), 0.85f);
            float tw = area.width * 0.78f, th = tw * 0.52f;
            Rect frame = new Rect(area.center.x - tw * 0.5f, area.center.y - th * 0.5f, tw, th);
            Panel(new Rect(frame.x + Px(3), frame.y + Px(6), frame.width, frame.height), new Color(0f, 0f, 0f, 0.45f), true);
            Panel(frame, unlocked ? new Color(0.42f, 0.24f, 0.11f) : new Color(0.25f, 0.25f, 0.25f), true);
            float rail = Px(13);
            Rect felt = new Rect(frame.x + rail, frame.y + rail, frame.width - 2f * rail, frame.height - 2f * rail);
            Fill(felt, cloth);
            Fill(new Rect(felt.x, felt.y, felt.width, felt.height * 0.5f), new Color(1f, 1f, 1f, 0.05f));
            // diamonds on the rail
            for (int i = 1; i < 8; i++)
            {
                if (i == 4) continue;
                float dx = Mathf.Lerp(felt.x, felt.xMax, i / 8f);
                DrawDisc(new Rect(dx - Px(2), frame.y + rail * 0.5f - Px(2), Px(4), Px(4)), new Color(1f, 1f, 1f, 0.6f));
                DrawDisc(new Rect(dx - Px(2), frame.yMax - rail * 0.5f - Px(2), Px(4), Px(4)), new Color(1f, 1f, 1f, 0.6f));
            }
            // pockets
            float ps = Px(17);
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
            {
                float px = Mathf.Lerp(felt.x + Px(2), felt.xMax - Px(2), i * 0.5f) - ps * 0.5f;
                float py = (j == 0 ? felt.y + Px(1) : felt.yMax - Px(1)) - ps * 0.5f;
                DrawDisc(new Rect(px, py, ps, ps), new Color(0.02f, 0.02f, 0.02f));
            }
            if (!unlocked) return;
            // balls: rack + cue ball + cue stick
            float bs = Px(14);
            int[] rack = { 1, 9, 2, 10, 8, 3, 11, 4, 12, 5 };
            int k = 0;
            for (int col = 0; col < 4; col++)
            for (int row = 0; row <= col; row++)
            {
                float bx = felt.x + felt.width * 0.66f + col * bs * 0.88f;
                float by = felt.center.y + (row - col * 0.5f) * bs;
                DrawBall(new Rect(bx - bs * 0.5f, by - bs * 0.5f, bs, bs), rack[k++ % rack.Length], 1f);
            }
            Vector2 cb = new Vector2(felt.x + felt.width * 0.26f, felt.center.y);
            DrawBall(new Rect(cb.x - bs * 0.5f, cb.y - bs * 0.5f, bs, bs), 0, 1f);
            Fill(new Rect(felt.x - rail, cb.y - Px(2), cb.x - bs * 0.7f - felt.x + rail, Px(4)), new Color(0.9f, 0.78f, 0.55f));
        }

        void DrawLock(Vector2 c)
        {
            float w = Px(46), h = Px(38);
            Rect body = new Rect(c.x - w * 0.5f, c.y - h * 0.15f, w, h);
            float ring = Px(36);
            DrawDisc(new Rect(c.x - ring * 0.5f, body.y - ring * 0.62f, ring, ring), new Color(0.9f, 0.9f, 0.92f));
            float hole = Px(22);
            DrawDisc(new Rect(c.x - hole * 0.5f, body.y - ring * 0.62f + (ring - hole) * 0.5f, hole, hole), new Color(0.1f, 0.1f, 0.12f));
            Panel(body, new Color(0.9f, 0.9f, 0.92f), true);
            DrawDisc(new Rect(c.x - Px(5), body.y + h * 0.3f, Px(10), Px(10)), new Color(0.2f, 0.2f, 0.22f));
        }

        /// <summary>Card frame with hover lift and glow. Returns the (possibly lifted) card rect.</summary>
        Rect CardFrame(Rect card, bool interactive, bool highlight, Color glow)
        {
            bool hover = interactive && card.Contains(Event.current.mousePosition);
            if (hover) card.y -= Px(6);
            Panel(new Rect(card.x + Px(4), card.y + Px(10), card.width, card.height), new Color(0f, 0f, 0f, 0.5f));
            if (hover || highlight)
                Panel(new Rect(card.x - Px(3), card.y - Px(3), card.width + Px(6), card.height + Px(6)), glow);
            Panel(card, new Color(0.085f, 0.095f, 0.115f, 0.98f));
            return card;
        }

        void DrawStages(float y)
        {
            Progression p = game.Progress;
            int n = p.Stages.Length;
            float cw = Px(350), ch = Px(436), gap = Px(26);
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Stage st = p.Stages[i];
                bool unlocked = p.IsUnlocked(i);
                Rect card = CardFrame(new Rect(x0 + i * (cw + gap), y, cw, ch), unlocked, false, new Color(1f, 0.8f, 0.25f, 0.95f));

                // art: cloth-coloured glow + table
                Rect art = new Rect(card.x + Px(10), card.y + Px(10), cw - Px(20), Px(178));
                Color cloth = unlocked ? st.clothColor : new Color(0.3f, 0.3f, 0.32f);
                Panel(art, new Color(cloth.r * 0.35f, cloth.g * 0.35f, cloth.b * 0.35f, 1f), true);
                GUI.BeginGroup(art);
                DrawDisc(new Rect(art.width * 0.5f - art.width * 0.7f, -art.height * 0.4f, art.width * 1.4f, art.height * 1.6f), new Color(cloth.r, cloth.g, cloth.b, 0.35f));
                GUI.EndGroup();
                DrawMiniTable(art, st.clothColor, unlocked);
                if (!unlocked)
                {
                    Panel(art, new Color(0f, 0f, 0f, 0.45f), true);
                    DrawLock(new Vector2(art.center.x, art.center.y - Px(4)));
                }

                GUI.Label(new Rect(card.x, card.y + Px(196), cw, Px(46)), $"<b>{st.name.ToUpper()}</b>", Style(center, 32, unlocked ? Color.white : new Color(1f, 1f, 1f, 0.5f)));

                // difficulty chip
                int d = Mathf.Clamp(st.difficulty, 0, 3);
                string chip = "AI  •  " + DifficultyNames[d];
                GUIStyle chipStyle = Style(small, 15, Color.white, TextAnchor.MiddleCenter);
                float chipW = chipStyle.CalcSize(new GUIContent(chip)).x + Px(30);
                Rect chipR = new Rect(card.center.x - chipW * 0.5f, card.y + Px(244), chipW, Px(28));
                Color dc = DifficultyColors[d];
                Panel(chipR, new Color(dc.r, dc.g, dc.b, unlocked ? 0.25f : 0.1f), true);
                GUI.Label(chipR, $"<b>{chip}</b>", Style(small, 15, unlocked ? dc : new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter));

                // rows
                Rect row1 = new Rect(card.x + Px(26), card.y + Px(284), cw - Px(52), Px(30));
                GUI.Label(row1, "Vstopnina", Style(small, 18, new Color(1f, 1f, 1f, 0.5f)));
                if (st.entryFee == 0) GUI.Label(row1, "<b>BREZPLAČNO</b>", Style(small, 18, new Color(0.45f, 0.95f, 0.55f), TextAnchor.MiddleRight));
                else CoinValue(row1, st.entryFee, Color.white);
                Rect row2 = new Rect(row1.x, row1.y + Px(32), row1.width, Px(30));
                GUI.Label(row2, "Nagrada", Style(small, 18, new Color(1f, 1f, 1f, 0.5f)));
                CoinValue(row2, st.reward, Gold);

                Rect btn = new Rect(card.x + Px(22), card.yMax - Px(72), cw - Px(44), Px(56));
                if (!unlocked)
                {
                    Panel(btn, new Color(1f, 1f, 1f, 0.05f), true);
                    GUI.Label(btn, $"<b>ODKLENE SE NA NIVOJU {st.requiredLevel}</b>", Style(small, 16, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter));
                }
                else if (p.Coins < st.entryFee)
                {
                    Panel(btn, new Color(1f, 1f, 1f, 0.05f), true);
                    GUI.Label(btn, "<b>PREMALO KOVANCEV</b>", Style(small, 16, new Color(1f, 0.5f, 0.45f), TextAnchor.MiddleCenter));
                }
                else if (GUI.Button(btn, "IGRAJ", goldPill))
                {
                    game.StartStage(i);
                }
            }

            // play with a friend
            float w2 = Px(640);
            Rect two = new Rect((Screen.width - w2) * 0.5f, y + ch + Px(24), w2, Px(70));
            if (GUI.Button(two, "", greenPill)) game.StartTwoPlayers();
            float bs = Px(36);
            DrawBall(new Rect(two.x + Px(22), two.center.y - bs * 0.5f, bs, bs), 1, 1f);
            DrawBall(new Rect(two.x + Px(46), two.center.y - bs * 0.5f, bs, bs), 9, 1f);
            GUI.Label(new Rect(two.x, two.y + Px(6), two.width, Px(34)), "<b>IGRAJ S PRIJATELJEM</b>", Style(center, 24, Color.white));
            GUI.Label(new Rect(two.x, two.y + Px(36), two.width, Px(26)), "2 igralca na istem računalniku  •  brezplačno", Style(center, 15, new Color(1f, 1f, 1f, 0.75f)));
        }

        void DrawShop(float y)
        {
            Progression p = game.Progress;
            int n = p.Cues.Length;
            float cw = Px(350), ch = Px(436), gap = Px(26);
            float x0 = (Screen.width - (n * cw + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                CueItem cue = p.Cues[i];
                bool owned = p.Owns(i);
                bool equipped = p.EquippedCue == i;
                Rect card = CardFrame(new Rect(x0 + i * (cw + gap), y, cw, ch), true, equipped,
                    equipped ? new Color(0.35f, 0.9f, 0.5f, 0.95f) : new Color(1f, 0.8f, 0.25f, 0.95f));

                Rect art = new Rect(card.x + Px(10), card.y + Px(10), cw - Px(20), Px(178));
                Panel(art, new Color(0.05f, 0.22f, 0.13f), true);
                GUI.BeginGroup(art);
                DrawDisc(new Rect(-art.width * 0.2f, -art.height * 0.5f, art.width * 1.4f, art.height * 1.6f), new Color(0.15f, 0.55f, 0.32f, 0.35f));
                GUI.EndGroup();
                DrawCuePreview(new Rect(art.x + Px(22), art.center.y - Px(10), art.width - Px(44), Px(20)), cue.shaftColor);

                if (equipped || owned)
                {
                    string tag = equipped ? "OPREMLJENA" : "V LASTI";
                    Rect tr = new Rect(art.xMax - Px(130), art.y + Px(10), Px(120), Px(26));
                    Panel(tr, equipped ? new Color(0.3f, 0.85f, 0.45f) : new Color(1f, 1f, 1f, 0.2f), true);
                    GUI.Label(tr, $"<b>{tag}</b>", Style(small, 13, equipped ? new Color(0.02f, 0.15f, 0.05f) : Color.white, TextAnchor.MiddleCenter));
                }

                GUI.Label(new Rect(card.x, card.y + Px(196), cw, Px(46)), $"<b>{cue.name.ToUpper()}</b>", Style(center, 32, Color.white));

                // stat bars
                StatBar(new Rect(card.x + Px(26), card.y + Px(250), cw - Px(52), Px(26)), "Pomožna črta", cue.aimAssist / 2.2f, $"x{cue.aimAssist:0.0}");
                StatBar(new Rect(card.x + Px(26), card.y + Px(282), cw - Px(52), Px(26)), "Moč", 0.25f + cue.powerBonus / 20f, $"+{cue.powerBonus:0} %");

                Rect btn = new Rect(card.x + Px(22), card.yMax - Px(72), cw - Px(44), Px(56));
                if (equipped)
                {
                    Panel(btn, new Color(1f, 1f, 1f, 0.05f), true);
                    GUI.Label(btn, "<b>OPREMLJENA</b>", Style(small, 18, new Color(0.45f, 0.95f, 0.55f), TextAnchor.MiddleCenter));
                }
                else if (owned)
                {
                    if (GUI.Button(btn, "OPREMI", greenPill)) { p.Equip(i); game.ApplyCue(); }
                }
                else if (p.Coins >= cue.price)
                {
                    if (GUI.Button(btn, "", goldPill) && p.Buy(i))
                    {
                        game.ApplyCue();
                        ShowBanner($"Kupil si palico {cue.name}!");
                    }
                    CoinLabelCentered(btn, "KUPI  ", cue.price, new Color(0.15f, 0.09f, 0.01f));
                }
                else
                {
                    Panel(btn, new Color(1f, 1f, 1f, 0.05f), true);
                    CoinLabelCentered(btn, "", cue.price, new Color(1f, 1f, 1f, 0.4f));
                }
            }
        }

        void DrawCuePreview(Rect r, Color shaft)
        {
            Panel(new Rect(r.x + Px(4), r.y + Px(8), r.width, r.height), new Color(0f, 0f, 0f, 0.35f), true);
            Panel(new Rect(r.x, r.y, r.width * 0.34f, r.height), new Color(0.16f, 0.08f, 0.04f), true);                       // butt
            Fill(new Rect(r.x + r.width * 0.1f, r.y, r.width * 0.12f, r.height), new Color(0.06f, 0.06f, 0.06f));             // wrap
            Fill(new Rect(r.x + r.width * 0.25f, r.y, r.width * 0.015f, r.height), new Color(0.85f, 0.85f, 0.8f));            // ring
            Fill(new Rect(r.x + r.width * 0.33f, r.y + r.height * 0.15f, r.width * 0.62f, r.height * 0.7f), shaft);           // shaft
            Fill(new Rect(r.x + r.width * 0.33f, r.y + r.height * 0.15f, r.width * 0.62f, r.height * 0.2f), new Color(1f, 1f, 1f, 0.25f));
            Fill(new Rect(r.x + r.width * 0.95f, r.y + r.height * 0.2f, r.width * 0.035f, r.height * 0.6f), new Color(0.96f, 0.96f, 0.92f)); // ferrule
            Fill(new Rect(r.x + r.width * 0.985f, r.y + r.height * 0.22f, r.width * 0.015f, r.height * 0.56f), new Color(0.2f, 0.45f, 0.95f)); // tip
        }

        void StatBar(Rect r, string label, float value, string text)
        {
            GUI.Label(new Rect(r.x, r.y, r.width * 0.42f, r.height), label, Style(small, 16, new Color(1f, 1f, 1f, 0.5f)));
            Rect bar = new Rect(r.x + r.width * 0.42f, r.center.y - Px(5), r.width * 0.38f, Px(10));
            Panel(bar, new Color(1f, 1f, 1f, 0.08f), true);
            Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * Mathf.Clamp01(value)), bar.height), Gold, true);
            GUI.Label(r, $"<b>{text}</b>", Style(small, 16, Color.white, TextAnchor.MiddleRight));
        }

        void CoinLabelCentered(Rect r, string prefix, int value, Color color)
        {
            GUIStyle st = Style(center, 22, color);
            st.fontStyle = FontStyle.Bold;
            string text = prefix + value.ToString("N0");
            float tw = st.CalcSize(new GUIContent(text)).x, cs = Px(26);
            float x = r.center.x - (tw + cs + Px(8)) * 0.5f;
            DrawCoin(new Rect(x, r.center.y - cs * 0.5f, cs, cs));
            GUI.Label(new Rect(x + cs + Px(8), r.y, tw + Px(4), r.height), text, new GUIStyle(st) { alignment = TextAnchor.MiddleLeft });
        }

        void DrawMenuBanner()
        {
            if (Time.unscaledTime >= bannerUntil || banner.Length == 0) return;
            float a = Mathf.Clamp01((bannerUntil - Time.unscaledTime) * 3f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            Rect r = new Rect(Screen.width * 0.5f - Px(420), Screen.height * 0.5f - Px(50), Px(840), Px(100));
            Panel(r, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            Panel(new Rect(r.x + Px(20), r.yMax - Px(8), r.width - Px(40), Px(5)), Gold, true);
            GUI.Label(r, banner, bannerStyle);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ menu drawing helpers

        static readonly Color Gold = new Color(1f, 0.8f, 0.25f);

        /// <summary>Copy of a style with a font size (in 1080p pixels), colour and alignment.</summary>
        GUIStyle Style(GUIStyle baseStyle, int size, Color color, TextAnchor? align = null)
        {
            var st = new GUIStyle(baseStyle) { fontSize = FontPx(size), richText = true, wordWrap = false };
            st.normal.textColor = color;
            if (align.HasValue) st.alignment = align.Value;
            return st;
        }

        /// <summary>Rounded panel (9-slice texture, corners keep their shape at any size). pill = smaller corner radius.</summary>
        void Panel(Rect r, Color c, bool pill = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            float min = Mathf.Min(r.width, r.height);
            GUIStyle st = pill || min < panelStyle.border.top * 2 ? pillStyle : panelStyle;
            if (min < st.border.top * 2) { Fill(r, c); return; }
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = c;
            st.Draw(r, false, false, false, false);
            GUI.backgroundColor = old;
        }

        void DrawCoin(Rect r)
        {
            DrawDisc(new Rect(r.x, r.y + r.height * 0.06f, r.width, r.height), new Color(0.45f, 0.28f, 0.02f));
            DrawDisc(r, new Color(0.95f, 0.66f, 0.12f));
            float i = r.width * 0.15f;
            DrawDisc(new Rect(r.x + i, r.y + i, r.width - 2f * i, r.height - 2f * i), new Color(1f, 0.84f, 0.3f));
            float j = r.width * 0.3f;
            DrawDisc(new Rect(r.x + j, r.y + j, r.width - 2f * j, r.height - 2f * j), new Color(0.95f, 0.7f, 0.15f));
            DrawDisc(new Rect(r.x + r.width * 0.22f, r.y + r.height * 0.16f, r.width * 0.25f, r.height * 0.18f), new Color(1f, 1f, 1f, 0.6f));
        }

        /// <summary>Right-aligned number with a small coin in front of it.</summary>
        void CoinValue(Rect row, int value, Color color)
        {
            GUIStyle st = Style(small, 19, color, TextAnchor.MiddleRight);
            st.fontStyle = FontStyle.Bold;
            string text = value.ToString("N0");
            float tw = st.CalcSize(new GUIContent(text)).x;
            GUI.Label(row, text, st);
            float cs = Px(22);
            DrawCoin(new Rect(row.xMax - tw - cs - Px(8), row.center.y - cs * 0.5f, cs, cs));
        }

        /// <summary>Builds the font, rounded button styles (normal / hover / pressed) and menu textures.</summary>
        void BuildMenuStyles()
        {
            // corner radius follows the screen size so shapes look the same at any resolution
            int pr = Mathf.Clamp(Px(22), 4, 40), sr = Mathf.Clamp(Px(9), 2, 16);
            roundTex = CachedRounded("panel", pr);
            pillTex = CachedRounded("pill", sr);
            panelStyle = new GUIStyle { border = new RectOffset(pr, pr, pr, pr) };
            panelStyle.normal.background = roundTex;
            pillStyle = new GUIStyle { border = new RectOffset(sr, sr, sr, sr) };
            pillStyle.normal.background = pillTex;

            goldPill = MakeButton("gold", new Color(1f, 0.86f, 0.36f), new Color(0.94f, 0.6f, 0.1f), new Color(0.16f, 0.09f, 0.01f), 26);
            greenPill = MakeButton("green", new Color(0.33f, 0.82f, 0.47f), new Color(0.1f, 0.5f, 0.26f), Color.white, 24);
            darkPill = MakeButton("dark", new Color(0.2f, 0.22f, 0.26f), new Color(0.12f, 0.13f, 0.16f), new Color(0.92f, 0.94f, 0.96f), 20);
            tabPill = MakeButton("tab", new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0.65f), 22);

            if (bgGradient == null)
            {
                bgGradient = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < 256; y++)
                {
                    float t = y / 255f; // 0 = bottom
                    bgGradient.SetPixel(0, y, Color.Lerp(new Color(0.012f, 0.016f, 0.02f), new Color(0.025f, 0.085f, 0.065f), t * t));
                }
                bgGradient.Apply();
            }
            if (vignette == null)
            {
                const int size = 128;
                vignette = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 1.414f;
                    vignette.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.SmoothStep(0.15f, 1f, d) * 0.7f));
                }
                vignette.Apply();
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D[]> buttonTextures = new System.Collections.Generic.Dictionary<string, Texture2D[]>();

        Texture2D CachedRounded(string key, int radius)
        {
            key += radius;
            if (!buttonTextures.TryGetValue(key, out Texture2D[] tex))
            {
                tex = new[] { MakeRounded(radius * 2 + 4, radius, Color.white, Color.white) };
                buttonTextures[key] = tex;
            }
            return tex[0];
        }

        GUIStyle MakeButton(string key, Color top, Color bottom, Color text, int fontSize)
        {
            int border = Mathf.Clamp(Px(16), 3, 28);
            int size = border * 2 + 8;
            key += border;
            if (!buttonTextures.TryGetValue(key, out Texture2D[] tex))
            {
                bool clear = top.a <= 0f;
                tex = new[]
                {
                    clear ? null : MakeRounded(size, border, top, bottom, true),
                    clear ? MakeRounded(size, border, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.08f))
                          : MakeRounded(size, border, Color.Lerp(top, Color.white, 0.2f), Color.Lerp(bottom, Color.white, 0.15f), true),
                    clear ? MakeRounded(size, border, new Color(1f, 1f, 1f, 0.14f), new Color(1f, 1f, 1f, 0.14f))
                          : MakeRounded(size, border, Color.Lerp(bottom, Color.black, 0.1f), Color.Lerp(top, Color.black, 0.15f))
                };
                buttonTextures[key] = tex;
            }
            var st = new GUIStyle
            {
                border = new RectOffset(border, border, border, border),
                alignment = TextAnchor.MiddleCenter,
                fontSize = FontPx(fontSize),
                fontStyle = FontStyle.Bold,
                richText = true,
                font = uiFont
            };
            st.normal.background = tex[0];
            st.hover.background = tex[1];
            st.active.background = tex[2];
            st.normal.textColor = text;
            st.hover.textColor = top.a <= 0f ? Color.white : text;
            st.active.textColor = text;
            return st;
        }

        /// <summary>Rounded rectangle texture: vertical gradient, anti-aliased corners, optional glossy top line.</summary>
        static Texture2D MakeRounded(int size, int radius, Color top, Color bottom, bool gloss = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                Color c = Color.Lerp(bottom, top, y / (float)(size - 1));
                if (gloss && y >= size - 1 - Mathf.Max(1, radius / 6)) c = Color.Lerp(c, Color.white, 0.3f);  // light top edge
                if (gloss && y < Mathf.Max(1, radius / 5)) c = Color.Lerp(c, Color.black, 0.25f);             // darker bottom edge
                c.a *= a;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ in-game HUD

        void DrawHud()
        {
            RulesEngine rules = game.Rules;
            if (rules == null) return;

            Shade(new Rect(0, 0, Screen.width, Px(108)), 0.55f);
            bool myTurn = rules.CurrentPlayer == 0;
            DrawBallTray(rules, 0, new Rect(Px(20), Px(54), Screen.width * 0.4f, Px(40)), false);
            DrawBallTray(rules, 1, new Rect(Screen.width * 0.6f - Px(20), Px(54), Screen.width * 0.4f, Px(40)), true);
            DrawShotClock(myTurn);
            string leftName = game.PlayerName(0);
            string rightName = game.TwoPlayers ? game.PlayerName(1) : $"AI ({game.Difficulty.displayName})";
            string me = $"{(myTurn ? "<color=#ffd54a>» </color>" : "")}<b>{(myTurn ? $"<color=#ffd54a>{leftName}</color>" : leftName)}</b>  {GroupText(rules, 0)}";
            string ai = $"{GroupText(rules, 1)}  <b>{(!myTurn ? $"<color=#ffd54a>{rightName}</color>" : rightName)}</b>{(!myTurn ? "<color=#ffd54a> «</color>" : "")}";
            GUI.Label(new Rect(Px(20), Px(10), Screen.width * 0.4f, Px(40)), me, normal);
            GUI.Label(new Rect(Screen.width * 0.6f, Px(10), Screen.width * 0.4f - Px(20), Px(40)), ai, new GUIStyle(normal) { alignment = TextAnchor.UpperRight });
            // whose turn: big label in the middle of the top bar
            if (game.State != GameState.GameOver)
            {
                string turnName = game.TwoPlayers || myTurn ? game.PlayerName(rules.CurrentPlayer) : "AI";
                string turn = game.TwoPlayers ? $"NA POTEZI: <color=#ffd54a>{turnName}</color>"
                                              : myTurn ? "<color=#ffd54a>TVOJA POTEZA</color>" : "NA POTEZI: <color=#ff8a7a>AI</color>";
                Rect tr = new Rect(Screen.width * 0.5f - Px(230), Px(6), Px(460), Px(46));
                Fill(tr, new Color(0f, 0f, 0f, 0.55f));
                Fill(new Rect(tr.x, tr.yMax - Px(4), tr.width, Px(4)), myTurn ? new Color(1f, 0.83f, 0.29f) : (game.TwoPlayers ? new Color(0.4f, 0.75f, 1f) : new Color(1f, 0.45f, 0.4f)));
                GUI.Label(tr, $"<b>{turn}</b>", new GUIStyle(center) { fontSize = FontPx(28) });
            }

            string status = game.State switch
            {
                GameState.BallInHand => game.BallInHandValid ? "Bela v roki: klikni, kamor jo postaviš" : "Bela v roki: tukaj se prekriva",
                GameState.AITurn => game.AI.Status,
                GameState.BallsMoving => "",
                _ => myTurn && rules.IsBreak ? "Razbij trikotnik!" : ""
            };
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(0, Px(112), Screen.width, Px(40)), status, center);

            DrawPocketCall();

            CueController cue = game.Cue;
            if (cue != null && game.State == GameState.PlayerAiming && cue.HumanControl) DrawSpinWidget(cue);
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

        /// <summary>8 Ball Pool style tray: the player's 7 balls, pocketed ones faded out, then the 8.</summary>
        void DrawBallTray(RulesEngine rules, int player, Rect area, bool alignRight)
        {
            BallGroup g = rules.GetGroup(player);
            float d = Px(34), gap = Px(6);
            int count = 8;
            float total = count * d + (count - 1) * gap;
            float x = alignRight ? area.xMax - total : area.x;
            int pocketed = 0;
            for (int i = 0; i < 7; i++)
            {
                Rect r = new Rect(x + i * (d + gap), area.y, d, d);
                if (g == BallGroup.None)
                {
                    DrawDisc(r, new Color(1f, 1f, 1f, 0.15f));
                    continue;
                }
                int n = g == BallGroup.Solids ? i + 1 : i + 9;
                bool onTable = rules.IsOnTable(n);
                if (!onTable) pocketed++;
                DrawBall(r, n, onTable ? 1f : 0.18f);
            }
            Rect eight = new Rect(x + 7 * (d + gap), area.y, d, d);
            bool eightTurn = g != BallGroup.None && rules.RemainingInGroup(player) == 0;
            DrawBall(eight, 8, eightTurn ? 1f : 0.25f);

            string label = g == BallGroup.None ? "" : $"Potopljene: <b>{pocketed}/7</b>";
            GUI.Label(new Rect(alignRight ? x - Px(190) : x + total + Px(12), area.y + Px(4), Px(180), d), label,
                new GUIStyle(small) { alignment = alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft });
        }

        void DrawBall(Rect r, int n, float alpha)
        {
            Color baseColor = BallColors[n <= 8 ? n : n - 8];
            if (n >= 9)
            {
                DrawDisc(r, new Color(0.96f, 0.95f, 0.9f, alpha));
                float inset = r.height * 0.2f;
                DrawDisc(new Rect(r.x + inset * 0.15f, r.y + inset, r.width - inset * 0.3f, r.height - 2f * inset), new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
            }
            else
            {
                DrawDisc(r, new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
            }
            if (n != 0)
            {
                float c = r.width * 0.5f;
                Rect inner = new Rect(r.x + c * 0.5f, r.y + c * 0.5f, c, c);
                DrawDisc(inner, new Color(1f, 1f, 1f, alpha));
                if (r.width >= 20f)
                {
                    Color old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    var num = new GUIStyle(ballNum) { fontSize = Mathf.Max(8, Mathf.RoundToInt(r.height * 0.34f)) };
                    GUI.Label(r, n.ToString(), num);
                    GUI.color = old;
                }
            }
            // glossy highlight
            DrawDisc(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.12f, r.width * 0.3f, r.height * 0.22f), new Color(1f, 1f, 1f, 0.45f * alpha));
        }

        void DrawShotClock(bool myTurn)
        {
            if (game.TurnTimeLimit <= 0f) return;
            GameState st = game.State;
            if (st != GameState.PlayerAiming && st != GameState.BallInHand && st != GameState.AITurn) return;

            float t = game.TurnTimeLeft / game.TurnTimeLimit;
            float w = Px(260), h = Px(12);
            Rect bar = new Rect(myTurn ? Px(20) : Screen.width - w - Px(20), Px(96), w, h);
            Fill(bar, new Color(0.1f, 0.1f, 0.1f, 0.9f));
            Color c = t > 0.33f ? new Color(0.3f, 0.85f, 0.4f) : t > 0.15f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.25f, 0.2f);
            Fill(new Rect(bar.x, bar.y, bar.width * t, bar.height), c);
            GUI.Label(new Rect(myTurn ? bar.xMax + Px(8) : bar.x - Px(70), bar.y - Px(9), Px(62), Px(30)),
                $"<b>{Mathf.CeilToInt(game.TurnTimeLeft)} s</b>", new GUIStyle(small) { alignment = myTurn ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight });
        }

        void DrawDisc(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, disc);
            GUI.color = old;
        }

        static Texture2D MakeDisc(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
            }
            tex.Apply();
            return tex;
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
            if (pauseSettings)
            {
                GUI.Label(new Rect(0, Px(60), Screen.width, Px(100)), "NASTAVITVE", title);
                DrawSettings(Px(190));
                if (GUI.Button(new Rect((Screen.width - Px(380)) * 0.5f, Screen.height - Px(110), Px(380), Px(70)), "Nazaj", big)) pauseSettings = false;
                return;
            }
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, Px(100)), "PAVZA", title);
            float w = Px(380), h = Px(70), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.4f;
            if (GUI.Button(new Rect(x, y, w, h), "Nadaljuj", big)) game.TogglePause();
            if (GUI.Button(new Rect(x, y + h + Px(14), w, h), "Nastavitve", big)) pauseSettings = true;
            if (GUI.Button(new Rect(x, y + 2f * (h + Px(14)), w, h), "Predaj in v meni", big)) { pauseSettings = false; game.Restart(); }
        }

        void DrawGameOver()
        {
            if (game.Rules == null) return;
            Shade(new Rect(0, 0, Screen.width, Screen.height), 0.65f);
            GameReward rw = game.LastReward;
            if (game.TwoPlayers)
            {
                DrawGameOverTwoPlayers();
                return;
            }
            GUI.Label(new Rect(0, Screen.height * 0.08f, Screen.width, Px(110)),
                rw.won ? "<color=#ffd54a>ZMAGA!</color>" : "PORAZ", new GUIStyle(title) { fontSize = FontPx(90) });

            float w = Px(760), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.24f;
            Shade(new Rect(x, y, w, Px(120)), 0.5f);
            GUI.Label(new Rect(x, y + Px(10), w, Px(50)), $"<color=#ffd54a><b>+{rw.coins:N0} kovancev</b></color>     <color=#6cc4ff><b>+{rw.xp} XP</b></color>",
                new GUIStyle(center) { fontSize = FontPx(36) });
            if (rw.levelsGained > 0)
                GUI.Label(new Rect(x, y + Px(62), w, Px(50)), $"<b>NOV NIVO {game.Progress.Level}!</b>  Odklepaš nove mize.", new GUIStyle(center) { fontSize = FontPx(30) });

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

        void DrawGameOverTwoPlayers()
        {
            GUI.Label(new Rect(0, Screen.height * 0.1f, Screen.width, Px(110)),
                $"<color=#ffd54a>ZMAGAL JE {game.PlayerName(game.Rules.Winner)}!</color>", new GUIStyle(title) { fontSize = FontPx(80) });

            float w = Px(760), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.3f;
            Shade(new Rect(x, y, w, Px(200)), 0.5f);
            PlayerStats a = game.Stats[0], b = game.Stats[1];
            GUI.Label(new Rect(x + Px(40), y + Px(16), w - Px(80), Px(180)),
                "<b>Statistika</b>                      IGRALEC 1  |  IGRALEC 2\n" +
                $"Streli:                             {a.shots}  |  {b.shots}\n" +
                $"Potopljene krogle:          {a.pocketed}  |  {b.pocketed}\n" +
                $"Natančnost:                    {a.Accuracy}%  |  {b.Accuracy}%\n" +
                $"Prekrški:                         {a.fouls}  |  {b.fouls}", normal);

            float bw = Px(360), bh = Px(70), by = y + Px(240);
            if (GUI.Button(new Rect(Screen.width * 0.5f - bw - Px(10), by, bw, bh), "Igraj znova", big)) game.PlayAgain();
            if (GUI.Button(new Rect(Screen.width * 0.5f + Px(10), by, bw, bh), "Glavni meni", big)) game.Restart();
        }

        // ------------------------------------------------------------------ intro: loading + controls

        /// <summary>Loading screen with a progress bar and changing tips.</summary>
        void DrawLoading()
        {
            float t = Time.unscaledTime - introStart;
            float progress = Mathf.Clamp01(t / LoadingTime);
            progress = 1f - Mathf.Pow(1f - progress, 2f); // eases out

            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.06f, 0.05f, 1f));
            // felt-green glow in the middle
            DrawDisc(new Rect(Screen.width * 0.5f - Px(420), Screen.height * 0.42f - Px(420), Px(840), Px(840)), new Color(0.05f, 0.35f, 0.18f, 0.25f));

            // spinning balls
            float cy = Screen.height * 0.36f;
            Color[] colors = { Color.white, new Color(1f, 0.82f, 0.1f), new Color(0.1f, 0.25f, 0.85f), new Color(0.85f, 0.12f, 0.1f), new Color(0.05f, 0.05f, 0.05f) };
            for (int i = 0; i < colors.Length; i++)
            {
                float phase = t * 4f - i * 0.6f;
                float bounce = Mathf.Abs(Mathf.Sin(phase)) * Px(30);
                float size = Px(54);
                float x = Screen.width * 0.5f + (i - 2) * Px(74) - size * 0.5f;
                DrawDisc(new Rect(x + Px(4), cy + Px(36), size, size * 0.35f), new Color(0f, 0f, 0f, 0.35f));
                DrawDisc(new Rect(x, cy - bounce, size, size), colors[i]);
                if (i == 4) DrawDisc(new Rect(x + size * 0.3f, cy - bounce + size * 0.3f, size * 0.4f, size * 0.4f), Color.white);
            }

            GUI.Label(new Rect(0, Screen.height * 0.47f, Screen.width, Px(100)), "BILJARD <color=#ffd54a>8-BALL</color>", title);

            float w = Px(640), h = Px(18), x0 = (Screen.width - w) * 0.5f, y0 = Screen.height * 0.64f;
            Fill(new Rect(x0, y0, w, h), new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(x0, y0, w * progress, h), new Color(1f, 0.83f, 0.29f, 1f));
            string tip = LoadingTips[Mathf.Min(LoadingTips.Length - 1, (int)(progress * LoadingTips.Length))];
            GUI.Label(new Rect(0, y0 + h + Px(14), Screen.width, Px(34)), $"{tip}  {Mathf.RoundToInt(progress * 100)} %", center);

            if (t >= LoadingTime + 0.3f) introPhase = 1;
        }

        /// <summary>First screen after loading: explains the controls before the game starts.</summary>
        void DrawControlsIntro()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.06f, 0.05f, 0.97f));
            GUI.Label(new Rect(0, Px(40), Screen.width, Px(90)), "KONTROLE", title);
            GUI.Label(new Rect(0, Px(120), Screen.width, Px(36)), "Preden začneš, si oglej, kako se igra.", center);

            string[,] rows =
            {
                { "MIŠKA", "Meri – palica se vrti okoli bele krogle" },
                { "LEVI GUMB", "Drži in povleci miško nazaj = moč, spusti = udarec" },
                { "DESNI GUMB", "Prekliči udarec" },
                { "SHIFT", "Natančno merjenje (počasnejše vrtenje)" },
                { "← → / A D", "Vrtenje palice s tipkovnico" },
                { "BELA KROGLA", "Spodaj desno: klikni, kje palica zadene belo (učinek / spin)" },
                { "KOLEŠČEK", "Približaj / oddalji mizo" },
                { "ESC", "Pavza in nastavitve" },
            };

            float w = Mathf.Min(Px(1000), Screen.width - Px(40)), x = (Screen.width - w) * 0.5f, y = Px(180), rowH = Px(64);
            int n = rows.GetLength(0);
            Fill(new Rect(x, y, w, rowH * n + Px(20)), new Color(1f, 1f, 1f, 0.05f));
            GUIStyle keyStyle = new GUIStyle(center) { fontStyle = FontStyle.Bold, fontSize = FontPx(20) };
            keyStyle.normal.textColor = new Color(0.05f, 0.05f, 0.05f);
            for (int i = 0; i < n; i++)
            {
                float ry = y + Px(10) + i * rowH;
                Rect key = new Rect(x + Px(24), ry + Px(10), Px(220), rowH - Px(20));
                Fill(key, new Color(1f, 0.83f, 0.29f, 1f));
                GUI.Label(key, rows[i, 0], keyStyle);
                GUI.Label(new Rect(key.xMax + Px(28), ry + Px(14), w - Px(290), Px(40)), rows[i, 1], normal);
            }

            float ty = y + rowH * n + Px(40);
            GUI.Label(new Rect(x, ty, w, Px(60)),
                "<b>Cilj:</b> potopi vse svoje krogle (polne 1–7 ali črtaste 9–15), nato črno 8 – ta mora pasti v ISTO luknjo kot tvoja zadnja krogla (označena je z rumeno 8). " +
                "Če zadeneš napačno kroglo ali potopiš belo, dobi nasprotnik belo v roko.", new GUIStyle(small) { alignment = TextAnchor.UpperCenter, wordWrap = true });

            float bw = Px(380), bh = Px(70);
            if (GUI.Button(new Rect((Screen.width - bw) * 0.5f, Screen.height - bh - Px(40), bw, bh), "Razumem – naprej", big))
                introPhase = 2;
        }

        // ------------------------------------------------------------------ calling the pocket for the black 8

        /// <summary>
        /// When a player is on the black 8 it must go into the same pocket as his last ball:
        /// that pocket gets a pulsing gold marker with an "8" and a hint is shown at the bottom.
        /// </summary>
        void DrawPocketCall()
        {
            if (game.Rules == null || game.Cue == null || game.Cue.cam == null) return;
            if (!game.NeedsPocketCall || game.CalledPocket < 0 || game.CalledPocket >= game.Pockets.Count) return;
            if (game.State == GameState.GameOver || game.State == GameState.MainMenu) return;

            Camera cam = game.Cue.cam;
            Vector3 sp = cam.WorldToScreenPoint(game.Pockets[game.CalledPocket]);
            if (sp.z <= 0f) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            float size = Px(64) + pulse * Px(10);
            Rect r = new Rect(sp.x - size * 0.5f, Screen.height - sp.y - size * 0.5f, size, size);
            DrawDisc(r, new Color(1f, 0.83f, 0.29f, 0.6f + 0.35f * pulse));
            float inner = size * 0.62f;
            DrawDisc(new Rect(sp.x - inner * 0.5f, Screen.height - sp.y - inner * 0.5f, inner, inner), new Color(0.05f, 0.05f, 0.05f));
            GUI.Label(r, "<b>8</b>", new GUIStyle(center) { fontSize = FontPx(24) });

            if (game.State == GameState.PlayerAiming || game.State == GameState.BallInHand || game.State == GameState.AITurn)
            {
                string who = game.IsHuman(game.Rules.CurrentPlayer) ? (game.TwoPlayers ? game.PlayerName(game.Rules.CurrentPlayer) : "Ti") : "AI";
                string text = $"<color=#ffd54a><b>NA VRSTI JE ČRNA 8</b></color> – {who} jo mora potopiti v <b>označeno luknjo</b> (kamor je šla zadnja krogla)";
                Rect tr = new Rect(Screen.width * 0.5f - Px(560), Screen.height - Px(80), Px(1120), Px(50));
                Fill(tr, new Color(0f, 0f, 0f, 0.6f));
                GUI.Label(tr, text, new GUIStyle(center) { fontSize = FontPx(21) });
            }
        }

        // ------------------------------------------------------------------ spin widget (8 Ball Pool style)

        /// <summary>Cue ball in the corner: click / drag on it to choose where the tip hits (top, back, side spin).</summary>
        void DrawSpinWidget(CueController cue)
        {
            float size = Px(150);
            Rect panel = new Rect(Screen.width - size - Px(40), Screen.height - size - Px(80), size + Px(20), size + Px(60));
            Rect ballRect = new Rect(panel.x + Px(10), panel.y + Px(10), size, size);
            if (Event.current.type == EventType.Layout) CueController.UiBlockers.Add(panel);

            Shade(panel, 0.45f);
            DrawDisc(new Rect(ballRect.x + Px(4), ballRect.y + Px(6), size, size), new Color(0f, 0f, 0f, 0.35f));
            DrawDisc(ballRect, new Color(0.97f, 0.96f, 0.92f));
            Vector2 c = ballRect.center;
            float r = size * 0.5f;
            Fill(new Rect(c.x - r * 0.8f, c.y - 0.5f, r * 1.6f, 1f), new Color(0f, 0f, 0f, 0.25f));
            Fill(new Rect(c.x - 0.5f, c.y - r * 0.8f, 1f, r * 1.6f), new Color(0f, 0f, 0f, 0.25f));

            // input
            Event e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 &&
                (ballRect.Contains(e.mousePosition) || (e.type == EventType.MouseDrag && panel.Contains(e.mousePosition))))
            {
                Vector2 d = (e.mousePosition - c) / (r * 0.8f);
                cue.Spin = new Vector2(d.x, -d.y);
                e.Use();
            }
            if (e.type == EventType.MouseDown && e.button == 1 && panel.Contains(e.mousePosition))
            {
                cue.Spin = Vector2.zero;
                e.Use();
            }

            Vector2 sp = cue.Spin;
            Vector2 dot = c + new Vector2(sp.x, -sp.y) * (r * 0.8f);
            float dr = Px(22);
            DrawDisc(new Rect(dot.x - dr * 0.5f - Px(2), dot.y - dr * 0.5f - Px(2), dr + Px(4), dr + Px(4)), Color.white);
            DrawDisc(new Rect(dot.x - dr * 0.5f, dot.y - dr * 0.5f, dr, dr), new Color(0.9f, 0.12f, 0.1f));

            string label = sp.sqrMagnitude < 0.0025f ? "Učinek: sredina" :
                (sp.y > 0.2f ? "naprej " : sp.y < -0.2f ? "nazaj " : "") + (sp.x > 0.2f ? "desno" : sp.x < -0.2f ? "levo" : "");
            if (label != "Učinek: sredina") label = "Učinek: " + label;
            GUI.Label(new Rect(panel.x, ballRect.yMax + Px(6), panel.width, Px(24)), label, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = FontPx(17) });
            GUI.Label(new Rect(panel.x, ballRect.yMax + Px(26), panel.width, Px(22)), "desni klik = sredina", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = FontPx(13) });
        }

        // ------------------------------------------------------------------ settings

        void DrawSettings(float y)
        {
            float w = Mathf.Min(Px(900), Screen.width - Px(40)), x = (Screen.width - w) * 0.5f;
            float rowH = Px(62);
            Rect panel = new Rect(x, y, w, rowH * 8 + Px(30));
            Panel(panel, new Color(0.085f, 0.095f, 0.115f, 0.97f));
            float ry = y + Px(15);

            EditorlessSlider(ref ry, x, w, "Občutljivost merjenja (miška)", ref GameSettings.AimSensitivity, 0.1f, 2f, "x{0:0.00}",
                "1.00 = palica kaže točno na miško, manj = počasnejše in natančnejše");
            EditorlessSlider(ref ry, x, w, "Natančno merjenje (Shift)", ref GameSettings.FineAimSensitivity, 0.02f, 0.5f, "x{0:0.00}",
                "kako počasi se palica vrti, ko držiš Shift");
            EditorlessSlider(ref ry, x, w, "Hitrost merjenja s puščicami / A-D", ref GameSettings.KeyboardAimSpeed, 0.1f, 3f, "x{0:0.00}", "");
            EditorlessSlider(ref ry, x, w, "Občutljivost moči", ref GameSettings.PowerSensitivity, 0.4f, 2.5f, "x{0:0.00}",
                "več = za polno moč potrebuješ krajši poteg miške");
            EditorlessSlider(ref ry, x, w, "Dolžina pomožne črte", ref GameSettings.AimLineLength, 0f, 2f, "x{0:0.00}", "");
            float vol = GameSettings.Volume * 100f;
            EditorlessSlider(ref ry, x, w, "Glasnost", ref vol, 0f, 100f, "{0:0} %", "");
            if (!Mathf.Approximately(vol, GameSettings.Volume * 100f)) { GameSettings.Volume = vol / 100f; GameSettings.Save(); }

            bool guide = GUI.Toggle(new Rect(x + Px(30), ry + Px(10), w * 0.5f, Px(36)), GameSettings.ShowGuide, "  Pomožne črte za merjenje", new GUIStyle(GUI.skin.toggle) { fontSize = FontPx(22) });
            if (guide != GameSettings.ShowGuide) { GameSettings.ShowGuide = guide; GameSettings.Save(); }
            if (GUI.Button(new Rect(x + w - Px(330), ry + Px(6), Px(300), Px(46)), "Privzete nastavitve", button)) GameSettings.ResetToDefaults();
        }

        /// <summary>One labelled slider row; saves the settings when the value changes.</summary>
        void EditorlessSlider(ref float y, float x, float w, string label, ref float value, float min, float max, string format, string hint)
        {
            GUI.Label(new Rect(x + Px(30), y, w * 0.45f, Px(32)), $"<b>{label}</b>", normal);
            if (hint.Length > 0) GUI.Label(new Rect(x + Px(30), y + Px(28), w * 0.5f, Px(26)), hint, new GUIStyle(small) { fontSize = FontPx(14) });
            Rect slider = new Rect(x + w * 0.52f, y + Px(14), w * 0.32f, Px(24));
            float v = GUI.HorizontalSlider(slider, value, min, max);
            GUI.Label(new Rect(slider.xMax + Px(16), y + Px(4), w * 0.14f, Px(32)), string.Format(format, v), normal);
            if (!Mathf.Approximately(v, value))
            {
                value = v;
                GameSettings.Save();
            }
            y += Px(62);
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
