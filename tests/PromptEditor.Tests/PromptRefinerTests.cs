using System.Diagnostics;
using System.Text;
using Xunit;

namespace PromptEditor.Tests;

public sealed class PromptRefinerTests
{
    [Fact]
    public void ContextKeepsOnlyTheHumanAndMainAgentConversation() => WithEvents("""
        {"type":"session.start","data":{"sessionId":"s"}}
        {"type":"user.message","data":{"content":"Investigate the failing login test","source":"user"}}
        {"type":"user.message","data":{"content":"<reminder>internal</reminder>","source":"system"}}
        {"type":"assistant.message","data":{"content":"","toolRequests":[{"name":"powershell"}]}}
        {"type":"tool.execution_complete","data":{"kind":"user.message","result":"tool output"}}
        {"type":"assistant.message","agentId":"explore-1","data":{"content":"subagent notes"}}
        {"type":"assistant.message","data":{"content":"The token expires early.\r\nFixed in auth.ts."}}
        {"type":"user.message","data":{"content":"Then update the docs"}}
        {"type":"user.message","data":{"content":"partial wr
        """, path =>
    {
        Assert.Equal("[User]\nInvestigate the failing login test\n\n[Copilot]\nThe token expires early.\n" +
                     "Fixed in auth.ts.\n\n[User]\nThen update the docs", PromptRefiner.ReadContext(path));
    });

    [Fact]
    public void ContextStartsFromTheLatestSuccessfulCompaction() => WithEvents("""
        {"type":"user.message","data":{"content":"old question"}}
        {"type":"session.compaction_complete","data":{"success":false,"summaryContent":"failed summary"}}
        {"type":"session.compaction_complete","data":{"success":true,"summaryContent":"We fixed auth."}}
        {"type":"user.message","data":{"content":"new question"}}
        """, path =>
    {
        Assert.Equal("[Summary of earlier conversation]\nWe fixed auth.\n\n[User]\nnew question",
            PromptRefiner.ReadContext(path));
    });

    [Fact]
    public void ContextKeepsTheNewestEntriesThatFitTheBudget() => WithEvents("""
        {"type":"user.message","data":{"content":"one"}}
        {"type":"user.message","data":{"content":"two"}}
        {"type":"user.message","data":{"content":"six"}}
        """, path => Assert.Equal("[User]\ntwo\n\n[User]\nsix", PromptRefiner.ReadContext(path, 24)));

    [Fact]
    public void LongContextEntriesAreTruncated() => WithEvents($$$"""
        {"type":"user.message","data":{"content":"short"}}
        {"type":"assistant.message","data":{"content":"{{{new string('x', PromptRefiner.EntryBudget + 50)}}}"}}
        """, path => Assert.Equal(
        "[User]\nshort\n\n[Copilot]\n" + new string('x', PromptRefiner.EntryBudget) + " \u2026[truncated]",
        PromptRefiner.ReadContext(path)));

    [Fact]
    public void SessionContextIsReadWhileTheLiveSessionHoldsTheLogOpen()
    {
        var home = Path.Combine(Path.GetTempPath(), $"prompt refine home {Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var directory = Path.Combine(home, "session-state", id.ToString("D"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var writer = new FileStream(Path.Combine(directory, "events.jsonl"), FileMode.Create,
                       FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.Write(Encoding.UTF8.GetBytes("{\"type\":\"user.message\",\"data\":{\"content\":\"caf\u00e9\"}}\n"));
                writer.Flush();
                Assert.Equal("[User]\ncaf\u00e9", PromptRefiner.ReadSessionContext(id.ToString("D"), home));
            }
            foreach (var invalid in new[] { null, "", "not-a-guid", @"..\..\secrets" })
                Assert.Equal("", PromptRefiner.ReadSessionContext(invalid, home));
            Assert.Equal("", PromptRefiner.ReadSessionContext(Guid.NewGuid().ToString(), home));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void RequestIncludesConversationOnlyWhenAvailable()
    {
        var withoutContext = PromptRefiner.BuildRequest("fix it\r\nplease", "");
        Assert.DoesNotContain("<conversation_context>", withoutContext);
        Assert.EndsWith("\n\n<draft_prompt>\nfix it\nplease\n</draft_prompt>\n", withoutContext);
        Assert.DoesNotContain('\r', withoutContext);
        var withContext = PromptRefiner.BuildRequest("fix it", "[User]\nlogin fails");
        Assert.Contains("\n\n<conversation_context>\n[User]\nlogin fails\n</conversation_context>\n\n<draft_prompt>",
            withContext);
    }

    [Theory]
    [InlineData("<refined_prompt>\nFix @src/app.cs\n</refined_prompt>\n", "draft", "Fix @src/app.cs")]
    [InlineData("Sure!\r\n<refined_prompt>One\r\nTwo</refined_prompt>\r\nDone.", "draft", "One\nTwo")]
    [InlineData("<refined_prompt>\npartial answer", "draft", "partial answer")]
    [InlineData("  untagged answer \n", "draft", "untagged answer")]
    [InlineData("\u001b[32m<refined_prompt>green</refined_prompt>\u001b[0m", "draft", "green")]
    [InlineData("<refined_prompt>\n```markdown\n# Title\nbody\n```\n</refined_prompt>", "title body", "# Title\nbody")]
    [InlineData("<refined_prompt>\n```\ncode\n```\n</refined_prompt>", "```\ncode\n```", "```\ncode\n```")]
    [InlineData("```\nRun:\n```sh\nls\n```\n```", "run ls", "```\nRun:\n```sh\nls\n```\n```")]
    public void ResponseExtractsTheRefinedPrompt(string output, string draft, string expected) =>
        Assert.Equal(expected, PromptRefiner.ParseResponse(output, draft));

    [Fact]
    public void CopilotIsFoundInPathOrderPreferringExecutables()
    {
        var root = Path.Combine(Path.GetTempPath(), $"prompt refine path {Guid.NewGuid():N}");
        var first = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(first, "copilot.cmd"), "");
            File.WriteAllText(Path.Combine(second, "copilot.exe"), "");
            Assert.Equal(Path.Combine(first, "copilot.cmd"),
                PromptRefiner.FindCopilot($"relative;\"{first}\";{second}"));
            File.WriteAllText(Path.Combine(first, "copilot.exe"), "");
            Assert.Equal(Path.Combine(first, "copilot.exe"), PromptRefiner.FindCopilot($"{first};{second}"));
            Assert.Null(PromptRefiner.FindCopilot(Path.Combine(root, "missing")));
            Assert.Null(PromptRefiner.FindCopilot(null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SessionFallsBackToTheNearestLiveSessionOwnedByAnAncestor()
    {
        var home = Path.Combine(Path.GetTempPath(), $"prompt refine home {Guid.NewGuid():N}");
        var holds = new List<FileStream>();
        string Session(string folder, int processId, bool live)
        {
            var directory = Path.Combine(home, "session-state", folder);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"inuse.{processId}.lock"), processId.ToString());
            var hold = Path.Combine(directory, $"inuse.{processId}.hold");
            File.WriteAllText(hold, "");
            if (live)
                holds.Add(new FileStream(hold, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
            return folder;
        }
        try
        {
            IReadOnlyList<int> ancestors = [10, 20, 30];
            Session(Guid.NewGuid().ToString("D"), 10, live: false);
            var expected = Session(Guid.NewGuid().ToString("D"), 20, live: true);
            Session(Guid.NewGuid().ToString("D"), 30, live: true);
            Session(Guid.NewGuid().ToString("D"), 40, live: true);
            Session("not-a-session", 10, live: true);

            Assert.Equal(expected, PromptRefiner.FindSessionId(null, home, () => ancestors));
            var fromEnvironment = Guid.NewGuid().ToString("D");
            Assert.Equal(fromEnvironment, PromptRefiner.FindSessionId(fromEnvironment, home,
                () => throw new InvalidOperationException("Ancestors are not needed.")));

            holds.ForEach(hold => hold.Dispose());
            Assert.Null(PromptRefiner.FindSessionId("", home, () => ancestors));
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ProcessTreeListsTheLaunchingProcessFirst()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        })!;
        try
        {
            var ancestors = ProcessTree.Ancestors(child.Id);
            Assert.NotEmpty(ancestors);
            Assert.Equal(Environment.ProcessId, ancestors[0]);
            Assert.DoesNotContain(child.Id, ancestors);
        }
        finally
        {
            child.Kill();
        }
    }

    [Fact]
    public async Task HelperGetsTheRequestOnStdinWithoutToolsOrTheLiveSessionId()
    {
        var previous = Environment.GetEnvironmentVariable("COPILOT_AGENT_SESSION_ID");
        Environment.SetEnvironmentVariable("COPILOT_AGENT_SESSION_ID", Guid.NewGuid().ToString());
        try
        {
            await WithFakeCopilot("""
                >"%~dp0args.txt" echo %*
                >"%~dp0session.txt" echo [%COPILOT_AGENT_SESSION_ID%]
                findstr "^" >"%~dp0request.txt"
                echo Here you go:
                echo ^<refined_prompt^>
                echo Fix the failing test in @src/app.cs
                echo ^</refined_prompt^>
                """, async (executable, directory) =>
            {
                var refined = await PromptRefiner.RefineAsync(executable, "fix test caf\u00e9", "[User]\nlogin",
                    CancellationToken.None);
                Assert.Equal("Fix the failing test in @src/app.cs", refined);
                var request = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "request.txt")));
                Assert.Contains("<draft_prompt>\nfix test caf\u00e9\n</draft_prompt>", request.Replace("\r\n", "\n"));
                Assert.Contains("[User]\nlogin", request.Replace("\r\n", "\n"));
                var arguments = File.ReadAllText(Path.Combine(directory, "args.txt"));
                Assert.Contains("--available-tools=prompt_editor_no_tools", arguments);
                Assert.DoesNotContain("--resume", arguments);
                Assert.DoesNotContain("--continue", arguments);
                Assert.Equal("[]", File.ReadAllText(Path.Combine(directory, "session.txt")).Trim());
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_AGENT_SESSION_ID", previous);
        }
    }

    [Theory]
    [InlineData(">&2 echo Error: not signed in\r\nexit /b 3", "exit code 3):\nError: not signed in")]
    [InlineData("exit /b 4", "exit code 4. Make sure you are signed in.")]
    [InlineData("echo ^<refined_prompt^>^</refined_prompt^>", "empty response")]
    public Task HelperFailuresAreReported(string script, string message) =>
        WithFakeCopilot(script, async (executable, _) =>
        {
            var error = await Assert.ThrowsAsync<PromptRefinerException>(() =>
                PromptRefiner.RefineAsync(executable, "draft", "", CancellationToken.None));
            Assert.Contains(message, error.Message);
        });

    [Fact]
    public Task CancellingStopsTheHelperPromptly() =>
        WithFakeCopilot("ping -n 60 127.0.0.1 >nul", async (executable, _) =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var started = Environment.TickCount64;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PromptRefiner.RefineAsync(executable, "draft", "", cancellation.Token));
            Assert.InRange(Environment.TickCount64 - started, 0, 15_000);
        });

    private static void WithEvents(string lines, Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prompt refine events {Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, lines.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        try
        {
            test(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task WithFakeCopilot(string script, Func<string, string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"prompt-refine-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "copilot.cmd");
        File.WriteAllText(executable, "@echo off\r\n" + script.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
        try
        {
            await test(executable, directory);
        }
        finally
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    // A killed batch file can release its handles shortly after it exits.
                    await Task.Delay(100);
                }
            }
        }
    }
}
