namespace KinetixModManager;

public class LogEntry
{
	public string Text { get; set; } = "";

	public int Index { get; set; }

	public override string ToString()
	{
		return Text;
	}
}
