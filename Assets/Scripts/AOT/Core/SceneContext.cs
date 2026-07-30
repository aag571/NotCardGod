using UnityEngine.SceneManagement;

    public sealed class SceneContext
    {
        public string TargetScene { get; set; }
        public GameMode Mode { get; set; }
        public string RoomAddress { get; set; }
        public string RoomId { get; set; }
        public string LocalId { get; set; }
        public string OpponentId { get; set; }
        public PlayerRole Role { get; set; }
        public static SceneContext Current { get; private set; }

        public static SceneContext GetOrCreate()
        {
            return Current ?? (Current = new SceneContext());
        }

        public static void Clear()
        {
            Current = null;
        }

        public void LoadThroughLoading(string target)
        {
            TargetScene = target;
            SceneManager.LoadScene("Loading");
        }
    }
