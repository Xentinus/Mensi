namespace Mensi.Core.Domain;

public class IntercourseEvent
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public bool? Protected { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
