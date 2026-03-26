namespace StardewAccessibleManager;

public class BackupItem
{
	public string Name { get; set; }

	public string FullPath { get; set; }

	public override string ToString()
	{
		return Name;
	}
}
