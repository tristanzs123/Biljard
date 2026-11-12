using System;
using UnityEngine;

namespace Billiards.Core
{
    /// <summary>One location / table the player can play at (like the cities in 8 Ball Pool).</summary>
    [Serializable]
    public class Stage
    {
        public string name;
        [Tooltip("Coins needed to enter.")] public int entryFee;
        [Tooltip("Coins won for a victory.")] public int reward;
        [Tooltip("Player level needed to unlock.")] public int requiredLevel;
        [Tooltip("Index into GameManager difficulties (0 = Lahka ... 3 = Profesionalna).")] public int difficulty;
        [Tooltip("Cloth colour of this table.")] public Color clothColor = Color.green;
        [Tooltip("XP for a victory.")] public int winXp;
    }

    /// <summary>A cue that can be bought in the shop.</summary>
    [Serializable]
    public class CueItem
    {
        public string name;
        public int price;
        [Tooltip("Aim assist length multiplier.")] public float aimAssist = 1f;
        [Tooltip("Extra power in percent.")] public float powerBonus;
        public Color shaftColor = new Color(1f, 0.86f, 0.62f);
    }

    /// <summary>
    /// Player progression: coins, XP, level, owned cues and the daily reward. Saved with PlayerPrefs.
    /// </summary>
    public class Progression : MonoBehaviour
    {
        const string KeyCoins = "bil_coins", KeyXp = "bil_xp", KeyOwned = "bil_cues", KeyCue = "bil_cue", KeyDaily = "bil_daily";

        [Tooltip("Coins of a brand new player.")]
        [SerializeField] int startCoins = 500;
        [Tooltip("Daily reward in coins.")]
        [SerializeField] int dailyReward = 150;

        [SerializeField] Stage[] stages =
        {
            new Stage { name = "Ljubljana",  entryFee = 0,   reward = 120,   requiredLevel = 1, difficulty = 0, winXp = 60,  clothColor = new Color(0.12f, 0.62f, 0.30f) },
            new Stage { name = "Maribor",    entryFee = 200,  reward = 450,   requiredLevel = 2, difficulty = 1, winXp = 100, clothColor = new Color(0.10f, 0.35f, 0.75f) },
            new Stage { name = "Ptuj",       entryFee = 600,  reward = 1400,  requiredLevel = 4, difficulty = 2, winXp = 160, clothColor = new Color(0.62f, 0.12f, 0.14f) },
            new Stage { name = "Las Vegas",  entryFee = 2000, reward = 5000,  requiredLevel = 6, difficulty = 3, winXp = 250, clothColor = new Color(0.38f, 0.16f, 0.55f) }
        };

        [SerializeField] CueItem[] cues =
        {
            new CueItem { name = "Začetna",    price = 0,    aimAssist = 1.0f, powerBonus = 0f,  shaftColor = new Color(1f, 0.86f, 0.62f) },
            new CueItem { name = "Hrastova",   price = 400,  aimAssist = 1.3f, powerBonus = 5f,  shaftColor = new Color(0.75f, 0.5f, 0.28f) },
            new CueItem { name = "Ebenovina",  price = 1500, aimAssist = 1.7f, powerBonus = 10f, shaftColor = new Color(0.2f, 0.16f, 0.14f) },
            new CueItem { name = "Zlata",      price = 5000, aimAssist = 2.2f, powerBonus = 15f, shaftColor = new Color(1f, 0.78f, 0.2f) }
        };

        /// <summary>Raised when coins, XP or items change.</summary>
        public event Action Changed;

        public Stage[] Stages => stages;
        public CueItem[] Cues => cues;
        public int Coins { get; private set; }
        public int Xp { get; private set; }
        public int EquippedCue { get; private set; }
        public int DailyRewardAmount => dailyReward;

        /// <summary>Player level computed from XP (each level needs 100 XP more than the previous one).</summary>
        public int Level
        {
            get
            {
                int level = 1, need = XpForNext(1), xp = Xp;
                while (xp >= need) { xp -= need; level++; need = XpForNext(level); }
                return level;
            }
        }

        /// <summary>XP gathered inside the current level and XP needed for the next one.</summary>
        public void LevelProgress(out int current, out int needed)
        {
            int level = 1, xp = Xp;
            needed = XpForNext(level);
            while (xp >= needed) { xp -= needed; level++; needed = XpForNext(level); }
            current = xp;
        }

        static int XpForNext(int level) => 100 + (level - 1) * 100;

        void Awake()
        {
            if (!PlayerPrefs.HasKey(KeyCoins)) PlayerPrefs.SetInt(KeyCoins, startCoins);
            Coins = PlayerPrefs.GetInt(KeyCoins, startCoins);
            Xp = PlayerPrefs.GetInt(KeyXp, 0);
            EquippedCue = Mathf.Clamp(PlayerPrefs.GetInt(KeyCue, 0), 0, cues.Length - 1);
        }

        /// <summary>Is the stage unlocked for the current level.</summary>
        public bool IsUnlocked(int stage) => Level >= stages[stage].requiredLevel;

        /// <summary>Takes coins if the player has enough.</summary>
        public bool TrySpend(int amount)
        {
            if (Coins < amount) return false;
            Coins -= amount;
            Save();
            return true;
        }

        /// <summary>Adds coins.</summary>
        public void AddCoins(int amount)
        {
            Coins += amount;
            Save();
        }

        /// <summary>Adds XP and returns how many levels were gained.</summary>
        public int AddXp(int amount)
        {
            int before = Level;
            Xp += amount;
            Save();
            return Level - before;
        }

        /// <summary>Is the cue owned.</summary>
        public bool Owns(int cue) => cue == 0 || (PlayerPrefs.GetInt(KeyOwned, 1) & (1 << cue)) != 0;

        /// <summary>Buys a cue (if affordable) and equips it.</summary>
        public bool Buy(int cue)
        {
            if (Owns(cue)) { Equip(cue); return true; }
            if (!TrySpend(cues[cue].price)) return false;
            PlayerPrefs.SetInt(KeyOwned, PlayerPrefs.GetInt(KeyOwned, 1) | (1 << cue));
            Equip(cue);
            return true;
        }

        /// <summary>Equips an owned cue.</summary>
        public void Equip(int cue)
        {
            if (!Owns(cue)) return;
            EquippedCue = cue;
            PlayerPrefs.SetInt(KeyCue, cue);
            Save();
        }

        /// <summary>Daily reward is available once per calendar day.</summary>
        public bool DailyAvailable => PlayerPrefs.GetString(KeyDaily, "") != DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>Collects the daily reward.</summary>
        public bool CollectDaily()
        {
            if (!DailyAvailable) return false;
            PlayerPrefs.SetString(KeyDaily, DateTime.Now.ToString("yyyy-MM-dd"));
            AddCoins(dailyReward);
            return true;
        }

        /// <summary>Emergency coins so the player can never get stuck at 0.</summary>
        public void EnsureMinimumCoins()
        {
            if (Coins < stages[0].entryFee) AddCoins(stages[0].entryFee * 2 - Coins);
        }

        void Save()
        {
            PlayerPrefs.SetInt(KeyCoins, Coins);
            PlayerPrefs.SetInt(KeyXp, Xp);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
