using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dodgeball
{
    /// <summary>
    /// Sets up the match (places characters, spawns balls), waits for the player to pick a difficulty,
    /// listens for deaths and ends the game.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum GameMode { Classic, SideBounce }

        /// <summary>Rules of the Side Bounce mode for one difficulty.</summary>
        [Serializable]
        public class SideBounceRules
        {
            [Tooltip("How many times a thrown ball may bounce off the side walls before it dies.")]
            [Min(1)] public int maxBounces = 2;
            [Tooltip("Fraction of its speed a ball keeps after each side-wall bounce.")]
            [Range(0.3f, 1f)] public float speedKept = 0.9f;
            [Tooltip("Chance (0..1) that the AI throws a bank shot off a side wall instead of a straight throw.")]
            [Range(0f, 1f)] public float aiBankShotChance = 0.35f;
            [Tooltip("Text shown on the difficulty button.")]
            public string description = "";

            public SideBounceRules() { }

            public SideBounceRules(int bounces, float kept, float bankChance, string text)
            {
                maxBounces = bounces; speedKept = kept; aiBankShotChance = bankChance; description = text;
            }
        }

        [Header("References")]
        [SerializeField] private Health player;
        [SerializeField] private Health opponent;
        [SerializeField] private Ball ballPrefab;

        [Header("Rules")]
        [Range(1, 3)] [SerializeField] private int ballCount = 3;
        [Tooltip("Show the Easy / Normal / Hard menu before the match. If off, 'Ai Difficulty' is used.")]
        [SerializeField] private bool showDifficultyMenu = true;
        [SerializeField] private AIController.Difficulty aiDifficulty = AIController.Difficulty.Normal;

        [Header("Side Bounce mode")]
        [Tooltip("Thrown balls in Side Bounce mode feel only this much gravity, so they fly flat enough to bank off the walls.")]
        [Range(0.1f, 1f)] [SerializeField] private float sideBounceGravityScale = 0.35f;
        [SerializeField] private SideBounceRules sideBounceEasy = new SideBounceRules(1, 0.75f, 0f, "1 bounce - AI throws straight");
        [SerializeField] private SideBounceRules sideBounceNormal = new SideBounceRules(2, 0.9f, 0.35f, "2 bounces - AI tries bank shots");
        [SerializeField] private SideBounceRules sideBounceHard = new SideBounceRules(4, 1f, 0.7f, "4 bounces - AI loves bank shots");

        [Header("Physics layers (created by the scene builder)")]
        [SerializeField] private string ballLayer = "Ball";
        [SerializeField] private string dividerLayer = "Divider";

        /// <summary>Set by Restart so the reloaded scene starts right away with the same difficulty.</summary>
        private static AIController.Difficulty? queuedDifficulty;
        /// <summary>Mode picked in the menu; kept across scene reloads (Restart / Menu).</summary>
        private static GameMode selectedMode = GameMode.Classic;

        public static GameManager Instance { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsGameOver { get; private set; }
        public AIController.Difficulty CurrentDifficulty { get; private set; }
        public GameMode CurrentMode { get; private set; }

        /// <summary>The mode the next match will use (set by the menu's mode buttons).</summary>
        public GameMode SelectedMode
        {
            get => selectedMode;
            set => selectedMode = value;
        }

        /// <summary>Side Bounce rules of the running match, or null in Classic mode.</summary>
        public SideBounceRules ActiveBounceRules =>
            (IsPlaying || IsGameOver) && CurrentMode == GameMode.SideBounce ? GetSideBounceRules(CurrentDifficulty) : null;

        /// <summary>Gravity multiplier for thrown (live) balls in the running match.</summary>
        public float LiveBallGravityScale => ActiveBounceRules != null ? sideBounceGravityScale : 1f;

        public SideBounceRules GetSideBounceRules(AIController.Difficulty difficulty)
        {
            switch (difficulty)
            {
                case AIController.Difficulty.Easy: return sideBounceEasy;
                case AIController.Difficulty.Hard: return sideBounceHard;
                default: return sideBounceNormal;
            }
        }

        /// <summary>true = player won.</summary>
        public event Action<bool> OnGameOver;
        public event Action OnMatchStarted;

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;

            // Balls fly over the center divider; only characters are blocked by it.
            int ball = LayerMask.NameToLayer(ballLayer);
            int divider = LayerMask.NameToLayer(dividerLayer);
            if (ball >= 0 && divider >= 0) Physics.IgnoreLayerCollision(ball, divider, true);
        }

        private void Start()
        {
            ArenaBounds arena = ArenaBounds.Instance;
            if (arena != null)
            {
                player.GetComponent<CharacterMotor>().Teleport(arena.SpawnPoint(-1) + Vector3.up * 1.1f);
                opponent.GetComponent<CharacterMotor>().Teleport(arena.SpawnPoint(1) + Vector3.up * 1.1f);
            }

            SpawnBalls();
            player.OnDied += HandleDeath;
            opponent.OnDied += HandleDeath;

            // Nobody moves until a difficulty is chosen.
            SetCharactersActive(false);

            if (queuedDifficulty.HasValue)
            {
                AIController.Difficulty d = queuedDifficulty.Value;
                queuedDifficulty = null;
                StartMatch(d);
            }
            else if (!showDifficultyMenu)
            {
                StartMatch(aiDifficulty);
            }
        }

        /// <summary>Called by the difficulty buttons.</summary>
        public void StartMatch(AIController.Difficulty difficulty)
        {
            if (IsPlaying || IsGameOver) return;
            CurrentDifficulty = difficulty;
            CurrentMode = selectedMode;
            AIController ai = opponent.GetComponent<AIController>();
            if (ai != null)
            {
                ai.SetDifficulty(difficulty);
                ai.SetBankShotChance(CurrentMode == GameMode.SideBounce ? GetSideBounceRules(difficulty).aiBankShotChance : 0f);
            }

            SetCharactersActive(true);
            IsPlaying = true;
            OnMatchStarted?.Invoke();
        }

        private void SpawnBalls()
        {
            if (ballPrefab == null || ArenaBounds.Instance == null) return;
            for (int i = 0; i < ballCount; i++)
            {
                // Spread the balls evenly along the center line.
                float t = ballCount == 1 ? 0f : Mathf.Lerp(-0.6f, 0.6f, i / (float)(ballCount - 1));
                Ball ball = Instantiate(ballPrefab);
                ball.Respawn(ArenaBounds.Instance.CenterLinePoint(t));
            }
        }

        private void HandleDeath(Health dead)
        {
            if (IsGameOver) return;
            IsGameOver = true;
            IsPlaying = false;
            SetCharactersActive(false);
            OnGameOver?.Invoke(dead == opponent);
        }

        /// <summary>Enables/disables input, AI and ball handling on both characters.</summary>
        private void SetCharactersActive(bool active)
        {
            foreach (Health h in new[] { player, opponent })
            {
                foreach (MonoBehaviour b in h.GetComponents<MonoBehaviour>())
                    if (b is PlayerController || b is AIController || b is BallThrower) b.enabled = active;
            }
        }

        /// <summary>Play again with the same mode and difficulty.</summary>
        public void Restart()
        {
            queuedDifficulty = CurrentDifficulty;
            ReloadScene();
        }

        /// <summary>Back to the difficulty menu.</summary>
        public void BackToMenu()
        {
            queuedDifficulty = null;
            ReloadScene();
        }

        private static void ReloadScene()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
        }
    }
}
