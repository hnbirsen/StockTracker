public class NotificationLog : BaseEntity
{
    public int UserId { get; set; }
    public int WatchGroupId { get; set; }
    public int Channel { get; set; } //// TODO: Change to enum
    public bool IsRead { get; set; } = false;
}