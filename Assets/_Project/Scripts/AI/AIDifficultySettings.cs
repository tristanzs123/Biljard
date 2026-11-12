using UnityEngine;

namespace Billiards.AI
{
    /// <summary>Tuning of one AI difficulty level.</summary>
    [CreateAssetMenu(menuName = "Billiards/AI Difficulty", fileName = "AIDifficulty")]
    public class AIDifficultySettings : ScriptableObject
    {
        [Tooltip("Name shown in the menu.")]
        public string displayName = "Srednja";

        [Tooltip("Aim error: standard deviation of Gaussian noise in degrees.")]
        [Range(0f, 5f)] public float angleErrorDeg = 1f;

        [Tooltip("Power error in percent (uniform +-).")]
        [Range(0f, 50f)] public float powerErrorPercent = 10f;

        [Tooltip("Consider where the cue ball will stop (position play).")]
        public bool usePosition = true;

        [Tooltip("Use spin (reserved - spin is not implemented yet).")]
        public bool useSpin;

        [Tooltip("Play a defensive shot when there is no good pot.")]
        public bool playSafety = true;

        [Tooltip("Chance to pick a random playable shot instead of the best one.")]
        [Range(0f, 1f)] public float badDecisionChance = 0.1f;

        [Tooltip("Minimum shot score for attempting a pot (below = safety, if enabled).")]
        [Range(0f, 1f)] public float minPotScore = 0.15f;

        [Tooltip("Thinking time range in seconds.")]
        public Vector2 thinkTime = new Vector2(0.8f, 1.6f);
    }
}
