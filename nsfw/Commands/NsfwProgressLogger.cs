using LibHac.Common;
using Spectre.Console;

namespace Nsfw.Commands;

public class NsfwProgressLogger : IProgressReport
{
    private long? _totalBlocks = null;
    private long _currentBlock = 0;
    private Dictionary<long, string> _sections = new ();

    public IEnumerable<TreeNode> GetReport()
    {
        return _sections.Select(section => new TreeNode(new Markup(section.Value)));
    }
    
    public void Report(long value)
    {
        throw new NotImplementedException();
    }

    public void ReportAdd(long value)
    {
        _currentBlock++;
    }

    public void SetTotal(long value)
    {
        _totalBlocks ??= value;
    }

    public void LogMessage(string message)
    {
        Console.WriteLine(message);
    }

    public void AddSection(int i)
    {
        _sections.Add(i, "[-/-]");
        _currentBlock = 0;
        _totalBlocks = null;
    }

    public void CloseSection(int index, Validity validity)
    {
        if (validity == Validity.Invalid)
        {
            _sections[index] = $"Section {index} -> [red]ERROR[/] " + $"[{_currentBlock}/{_totalBlocks} Blocks]".EscapeMarkup();
        }
        else
        {
            _sections[index] = $"Section {index} -> [green]VALID[/] " + $"[{_currentBlock}/{_totalBlocks} Blocks]".EscapeMarkup();
        }
    }
}