namespace monitor_desktop.Models
{
    public class ApiResponse<T>
    {
        public int Status { get; set; }

        public string Message { get; set; }

        public T Data { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}