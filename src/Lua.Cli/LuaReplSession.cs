using System.Text;
using Lua.Compiler;
using Lua.Runtime.Execution;
using Lua.Runtime.Values;
using Lua.Syntax.Lexing;

namespace Lua.Cli;

public sealed class LuaReplSession
{
    private readonly LuaCliApplication _application;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public LuaReplSession(
        LuaCliApplication application,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public void Run(bool showBanner)
    {
        if (showBanner)
        {
            _output.WriteLine(LuaCliApplication.VersionText);
        }

        var buffer = new StringBuilder();
        while (true)
        {
            _output.Write(buffer.Length == 0 ? "> " : ">> ");
            _output.Flush();

            var line = _input.ReadLine();
            if (line is null)
            {
                return;
            }

            if (buffer.Length > 0)
            {
                buffer.AppendLine();
            }

            buffer.Append(line);
            var submission = buffer.ToString();

            try
            {
                var results = _application.EvaluateTextChunk(NormalizeSubmission(submission), "=(repl)");
                PrintResults(results);
                buffer.Clear();
            }
            catch (LuaSyntaxException ex) when (IsIncompleteInput(ex))
            {
                continue;
            }
            catch (LuaSyntaxException ex)
            {
                _error.WriteLine(ex.Message);
                buffer.Clear();
            }
            catch (LuaCompilerException ex)
            {
                _error.WriteLine(ex.Message);
                buffer.Clear();
            }
            catch (LuaRuntimeException ex)
            {
                _error.WriteLine(_application.FormatLuaError(ex.ErrorObject));
                buffer.Clear();
            }
        }
    }

    private void PrintResults(IReadOnlyList<LuaValue> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        var rendered = new string[results.Count];
        for (var index = 0; index < results.Count; index++)
        {
            rendered[index] = _application.FormatResult(results[index]);
        }

        _output.WriteLine(string.Join('\t', rendered));
    }

    private static string NormalizeSubmission(string submission)
    {
        if (string.IsNullOrWhiteSpace(submission))
        {
            return submission;
        }

        var trimmed = submission.TrimStart();
        if (!trimmed.StartsWith('='))
        {
            return submission;
        }

        var prefixLength = submission.Length - trimmed.Length;
        return submission[..prefixLength] + "return " + trimmed[1..];
    }

    private static bool IsIncompleteInput(LuaSyntaxException exception)
    {
        return exception.Message.Contains("unfinished string", StringComparison.Ordinal) ||
               exception.Message.Contains("unfinished long", StringComparison.Ordinal) ||
               exception.Message.Contains("expected 'end'", StringComparison.Ordinal) ||
               exception.Message.Contains("expected 'until'", StringComparison.Ordinal) ||
               exception.Message.Contains("expected ')'", StringComparison.Ordinal) ||
               exception.Message.Contains("expected '}'", StringComparison.Ordinal) ||
               exception.Message.Contains("expected ']'", StringComparison.Ordinal) ||
               exception.Message.Contains("expected expression", StringComparison.Ordinal);
    }
}
