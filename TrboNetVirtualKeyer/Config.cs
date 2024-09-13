namespace TrboNetVirtualKeyer
{
    public class Config
    {
        public bool IsVoxEnabled { get; set; } = true;
        public float VoxThreshold { get; set; } = 0.1f;
        public int VoxHangTime { get; set; } = 3000;
        public int DebounceTime { get; set; } = 500;
        public int SelectedDeviceIndex { get; set; } = 0;
    }
}
