using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Engine.Scripts;
using Xunit;
using static BDIT.TenantToolkit.Tests.ExchangeReportRunnerTests;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// INT-088 schema 2 admission. The engine's report registration is authoritative and the manifest may only repeat it;
/// version dispatch is explicit; a copy-only change can never become runnable; and a body that names a read outside the
/// gate is refused when the library loads, before any run could be offered.
/// </summary>
public sealed class ScriptLibrarySchema2Tests
{
    private static void Refused(IReadOnlyDictionary<string, byte[]> files) =>
        Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));

    [Fact]
    public void A_reviewed_schema_2_item_is_admitted_and_the_shipped_library_keeps_schema_1()
    {
        var entry = ScriptCatalogue.Load(Library()).Find("exo.synthetic-mailbox-inventory");
        Assert.Equal(2, entry.Manifest.SchemaVersion);
        Assert.Equal("exo-mailbox-inventory", ScriptCatalogue.ReportFor(entry.Manifest).Id);
        // No shipped item is upgraded by this slice: every one stays a schema 1, Copy-only manual item.
        Assert.All(ScriptCatalogue.Shipped.Entries, e => { Assert.Equal(1, e.Manifest.SchemaVersion); Assert.Null(e.Manifest.Execution); });
    }

    [Fact]
    public void Version_dispatch_is_explicit_in_both_directions()
    {
        // A schema 1 registry cannot carry a schema 2 manifest, nor declare versions at all.
        Refused(Library(registry: r => { r["schemaVersion"] = 1; r["scripts"]![0]!.AsObject().Remove("manifestSchemaVersion"); }));
        Refused(Library(registry: r => r["schemaVersion"] = 1));
        // A schema 2 registry must declare each version, and the declaration must match the pinned manifest.
        Refused(Library(registry: r => r["scripts"]![0]!.AsObject().Remove("manifestSchemaVersion")));
        Refused(Library(registry: r => r["scripts"]![0]!["manifestSchemaVersion"] = 1));
        Refused(Library(registry: r => r["scripts"]![0]!["manifestSchemaVersion"] = 3));
        Refused(Library(registry: r => r["schemaVersion"] = 3));
        // Schema 1 cannot borrow an execution member; schema 2 cannot omit it.
        Refused(Library(manifest: m => m["schemaVersion"] = 1, registry: r => r["scripts"]![0]!["manifestSchemaVersion"] = 1));
        Refused(Library(manifest: m => m.Remove("execution")));
    }

    [Fact]
    public void A_copy_only_change_can_never_be_admitted_for_run()
    {
        Refused(Library(manifest: m => m["mode"] = "copyOnlyChange"));
        Assert.Throws<ConfigurationException>(() => ScriptCatalogue.ReportFor(ScriptCatalogue.Shipped.Entries.First().Manifest));
    }

    [Fact]
    public void Runnable_items_have_lower_caps()
    {
        Refused(Library(timeoutSeconds: 1801));
        Refused(Library(manifest: m => m["limits"]!["maximumRows"] = 10001));
        Assert.NotNull(ScriptCatalogue.Load(Library(timeoutSeconds: 1800)));
    }

    [Fact]
    public void The_manifest_can_only_repeat_the_engines_registration()
    {
        Refused(Library(manifest: m => m["execution"]!["runnerTemplateSha256"] = new string('0', 64)));
        Refused(Library(manifest: m => m["execution"]!["reportId"] = "exo-unregistered"));
        Refused(Library(manifest: m => m["execution"]!["adapter"] = "purview"));
        Refused(Library(manifest: m => m["execution"]!["outputKind"] = "csv"));
        Refused(Library(manifest: m => m["execution"]!["reportSchemaVersion"] = 2));
        Refused(Library(manifest: m => m["execution"]!["outputSchemaVersion"] = 2));
        Refused(Library(manifest: m => m["execution"]!["script"] = "Set-Mailbox"));
        Refused(Library(manifest: m => m["modules"]![0]!["name"] = "ExchangeOnline.Other"));
        // Columns are the registered projection, exactly and in order.
        Refused(Library(manifest: m => m["outputSchema"]!["columns"]!.AsArray().RemoveAt(0)));
        Refused(Library(manifest: m => m["outputSchema"]!["columns"]!.AsArray().Add("totalItemSize")));
        Refused(Library(manifest: m =>
        {
            var columns = m["outputSchema"]!["columns"]!.AsArray();
            var first = columns[0]!.GetValue<string>(); columns[0] = columns[1]!.GetValue<string>(); columns[1] = first;
        }));
        // Parameters are the registered parameters, by name, type and shape.
        Refused(Library(manifest: m => m["parameters"]![0]!["name"] = "IncludeArchives"));
        Refused(Library(manifest: m => m["parameters"]![0]!["type"] = "string"));
        Refused(Library(manifest: m => m["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "ResultSize", ["label"] = "Rows", ["type"] = "integer", ["help"] = "Rows.", ["minimum"] = 1, ["maximum"] = 10 })));
        Refused(Library(manifest: m => m["parameters"] = new JsonArray()));
    }

    [Theory]
    [InlineData("param([bool]$IncludeArchive)\nGet-EXOMailbox\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Set-Mailbox' -Parameters @{}\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\nSet-Mailbox -Identity x\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\nGet-Module\n")]
    [InlineData("param([bool]$IncludeArchive)\n$c = 'Get-EXOMailbox'\n& $c\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command $c -Parameters @{}\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\n$global:x = 1\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\n$ExecutionContext.InvokeCommand\n")]
    [InlineData("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\n[scriptblock]::Create('x')\n")]
    [InlineData("param([bool]$IncludeArchive)\nfunction Use-BditRead { 1 }\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\n")]
    [InlineData("param([bool]$IncludeArchive)\n$IncludeArchive\n")]
    public void A_body_that_reads_outside_the_gate_is_refused_when_the_library_loads(string body) => Refused(Library(body));
}
