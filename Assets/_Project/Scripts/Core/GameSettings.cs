using System;
using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// Player settings (Nastavitve): aim / fine aim / power sensitivity, aim line length, guide and volume.
    /// Saved with PlayerPrefs, read by CueController and the AudioManager.
    /// </summary>
    public static class GameSettings
    {
        const string KeyAim = "set_aim", KeyFine = "set_fine", KeyPower = "set_power",
                     KeyLine = "set_line", KeyGuide = "set_guide", KeyVolume = "set_volume", KeyKeys = "set_keys";

        public const float DefaultAim = 1f, DefaultFine = 0.15f, DefaultPower = 1f, DefaultLine = 1f, DefaultVolume = 0.8f, DefaultKeys = 1f;

        /// <summary>How fast the cue turns with the mouse (1 = cue points exactly at the mouse).</summary>
        public static float AimSensitivity = DefaultAim;
        /// <summary>Multiplier while holding Shift (fine aim).</summary>
        public static float FineAimSensitivity = DefaultFine;
        /// <summary>Power sensitivity (higher = less pulling needed for full power).</summary>
        public static float PowerSensitivity = DefaultPower;
        /// <summary>Aim line length multiplier.</summary>
        public static float AimLineLength = DefaultLine;
        /// <summary>Show the aim guide (lines + ghost ball).</summary>
        public static bool ShowGuide = true;
        /// <summary>Master volume 0..1.</summary>
        public static float Volume = DefaultVolume;
        /// <summary>Aim speed with the arrow keys / A-D.</summary>
        public static float KeyboardAimSpeed = DefaultKeys;

        /// <summary>Raised after settings change.</summary>
        public static event Action Changed;

        static bool loaded;

        /// <summary>Loads the settings once (safe to call many times).</summary>
        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            AimSensitivity = PlayerPrefs.GetFloat(KeyAim, DefaultAim);
            FineAimSensitivity = PlayerPrefs.GetFloat(KeyFine, DefaultFine);
            PowerSensitivity = PlayerPrefs.GetFloat(KeyPower, DefaultPower);
            AimLineLength = PlayerPrefs.GetFloat(KeyLine, DefaultLine);
            ShowGuide = PlayerPrefs.GetInt(KeyGuide, 1) == 1;
            Volume = PlayerPrefs.GetFloat(KeyVolume, DefaultVolume);
            KeyboardAimSpeed = PlayerPrefs.GetFloat(KeyKeys, DefaultKeys);
            AudioListener.volume = Volume;
        }

        /// <summary>Saves and applies the settings.</summary>
        public static void Save()
        {
            PlayerPrefs.SetFloat(KeyAim, AimSensitivity);
            PlayerPrefs.SetFloat(KeyFine, FineAimSensitivity);
            PlayerPrefs.SetFloat(KeyPower, PowerSensitivity);
            PlayerPrefs.SetFloat(KeyLine, AimLineLength);
            PlayerPrefs.SetInt(KeyGuide, ShowGuide ? 1 : 0);
            PlayerPrefs.SetFloat(KeyVolume, Volume);
            PlayerPrefs.SetFloat(KeyKeys, KeyboardAimSpeed);
            PlayerPrefs.Save();
            AudioListener.volume = Volume;
            Changed?.Invoke();
        }

        /// <summary>Back to the default values.</summary>
        public static void ResetToDefaults()
        {
            AimSensitivity = DefaultAim;
            FineAimSensitivity = DefaultFine;
            PowerSensitivity = DefaultPower;
            AimLineLength = DefaultLine;
            ShowGuide = true;
            Volume = DefaultVolume;
            KeyboardAimSpeed = DefaultKeys;
            Save();
        }
    }
}
