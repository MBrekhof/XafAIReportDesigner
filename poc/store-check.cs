#:project ../XafAIReportDesigner/XafAIReportDesigner.Module/XafAIReportDesigner.Module.csproj
#:property PublishAot=false

// DB-backed self-check for ReportDataV2Store (RPT-010/011): create-only Insert, atomic
// SaveIfUnchanged, Delete guard. Needs the dev PostgreSQL (docker start xaf-postgres).
// Run:  dotnet run poc/store-check.cs        (from the repo root)

using XafAIReportDesigner.Module.Services;

var store = new ReportDataV2Store("Host=localhost;Port=5432;Database=xafaireportdesigner;Username=xaf;Password=xaf123");
var name = "zz-store-check-" + Guid.NewGuid().ToString("N")[..8];
byte[] v1 = [1, 2, 3], v2 = [4, 5, 6], v3 = [7, 8, 9];

try
{
    store.Insert(name, v1);
    Assert(store.Exists(name) && store.Load(name)!.SequenceEqual(v1), "Insert creates the row");
    Assert(Throws(() => store.Insert(name, v2)), "Insert on an existing name throws (unique index)");
    Assert(Throws(() => store.Insert("bad/name", v1)), "Insert rejects a name with a slash");

    Assert(store.SaveIfUnchanged(name, v2, expected: v1), "SaveIfUnchanged with the current bytes writes");
    Assert(store.Load(name)!.SequenceEqual(v2), "…and the row now holds the new bytes");
    Assert(!store.SaveIfUnchanged(name, v3, expected: v1), "SaveIfUnchanged with STALE bytes writes nothing");
    Assert(store.Load(name)!.SequenceEqual(v2), "…and the row is untouched");

    Assert(store.List().Any(r => r.Name == name && !r.IsPredefined), "List reports the row as not predefined");
    Assert(store.Delete(name), "Delete removes it");
    Assert(!store.Exists(name), "…and it is gone");
    Assert(!store.Delete(name), "Delete of a missing row returns false");
    Console.WriteLine("store-check: all assertions passed");
    return 0;
}
finally
{
    if (store.Exists(name)) store.Delete(name);
}

static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

static void Assert(bool ok, string what)
{
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
    if (!ok) throw new Exception("store-check failed: " + what); // throw, so the finally still deletes the row
}
