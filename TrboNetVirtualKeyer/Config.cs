namespace TrboNetVirtualKeyer
{
    public class Config
    {
        public bool IsVoxEnabled { get; set; }
        public float VoxThreshold { get; set; }
        public int VoxHangTime { get; set; }
        public int DebounceTime { get; set; }
        public int SelectedDeviceIndex { get; set; }
        public string? ComPort { get; set; }
    }
}
