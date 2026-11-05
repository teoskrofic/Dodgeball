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
        [Header("References")]
        [SerializeField] private Health player;
        [SerializeField] private Health opponent;
        [SerializeField] private Ball ballPrefab;

        [Header("Rules")]
        [Range(1, 3)] [SerializeField] private int ballCount = 3;
        [Tooltip("Show the Easy / Normal / Hard menu before the match. If off, 'Ai Difficulty' is used.")]
        [SerializeField] private bool showDifficultyMenu = true;
        [SerializeField] private AIController.Difficulty aiDifficulty = AIController.Difficulty.Normal;

        [Header("Physics layers (created by the scene builder)")]
        [SerializeField] private string ballLayer = "Ball";
        [SerializeField] private string dividerLayer = "Divider";

        /// <summary>Set by Restart so the reloaded scene starts right away with the same difficulty.</summary>
        private static AIController.Difficulty? queuedDifficulty;

        public static GameManager Instance { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsGameOver { get; private set; }
        public AIController.Difficulty CurrentDifficulty { get; private set; }

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
            AIController ai = opponent.GetComponent<AIController>();
            if (ai != null) ai.SetDifficulty(difficulty);

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

        /// <summary>Play again with the same difficulty.</summary>
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
