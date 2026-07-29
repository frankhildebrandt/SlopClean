using SlopClean.Core.Abstractions;
using SlopClean.Core.Backup;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;
using SlopClean.Core.Settings;
using SlopClean.Core.Tests.Fakes;

namespace SlopClean.Core.Tests;

public class BackupServiceTests
{
    [Fact]
    public async Task File_backup_round_trip_restores_deleted_file()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\Temp\a.tmp", 42);
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var backup = new BackupService(fs, new FakeRegistryStore(fs), store, new SafetyPolicy(fs));

        var action = new OptimizationAction(
            "a1", "temp-cleaner", "f1", PrivilegedOperationCodes.DeleteFile,
            @"C:\Users\Test\AppData\Local\Temp\a.tmp",
            @"C:\Users\Test\AppData\Local\Temp",
            RequiredPrivilege.None);

        var pending = await backup.CreatePendingBackupAsync(action, "a.tmp");
        Assert.NotNull(pending);
        await store.CommitAsync(pending!.Id);

        fs.DeleteFile(action.Path!);
        Assert.False(fs.FileExists(action.Path!));

        var result = await backup.RestoreAsync(pending.Id);
        Assert.Equal(ApplyOutcome.Succeeded, result.Outcome);
        Assert.True(fs.FileExists(action.Path!));
        Assert.Equal(42, fs.GetFileInfo(action.Path!)!.Length);
    }

    [Fact]
    public async Task Directory_backup_round_trip_restores_tree()
    {
        var fs = CreateFs();
        fs.AddFile(@"C:\Users\Test\AppData\Local\GoneApp\cache.dat", 100);
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var backup = new BackupService(fs, new FakeRegistryStore(fs), store, new SafetyPolicy(fs));

        var action = new OptimizationAction(
            "a1", "uninstall-cleanup", "f1", PrivilegedOperationCodes.DeleteDirectory,
            @"C:\Users\Test\AppData\Local\GoneApp",
            @"C:\Users\Test\AppData\Local",
            RequiredPrivilege.None);

        var pending = await backup.CreatePendingBackupAsync(action, "GoneApp");
        Assert.NotNull(pending);
        await store.CommitAsync(pending!.Id);

        fs.DeleteDirectory(action.Path!, recursive: true);
        Assert.False(fs.DirectoryExists(action.Path!));

        var result = await backup.RestoreAsync(pending.Id);
        Assert.Equal(ApplyOutcome.Succeeded, result.Outcome);
        Assert.True(fs.FileExists(@"C:\Users\Test\AppData\Local\GoneApp\cache.dat"));
    }

    [Fact]
    public void Empty_recycle_bin_is_not_backupable()
    {
        var fs = CreateFs();
        var settings = new InMemorySettingsStore(@"C:\Backups");
        var store = new RestorePointStore(fs, settings);
        var backup = new BackupService(fs, new FakeRegistryStore(fs), store, new SafetyPolicy(fs));

        var action = new OptimizationAction(
            "a1", "recycle-bin", "f1", PrivilegedOperationCodes.EmptyRecycleBin,
            null, null, RequiredPrivilege.None);

        Assert.False(backup.CanCreateBackup(action));
        Assert.False(OptimizationPlan.IsRestorableOperation(action.OperationCode));
    }

    private static FakeFileSystem CreateFs()
    {
        var fs = new FakeFileSystem();
        fs.Folders[SpecialFolderKind.Windows] = @"C:\Windows";
        fs.Folders[SpecialFolderKind.System] = @"C:\Windows\System32";
        fs.Folders[SpecialFolderKind.UserProfile] = @"C:\Users\Test";
        fs.Folders[SpecialFolderKind.ApplicationData] = @"C:\Users\Test\AppData\Roaming";
        fs.Folders[SpecialFolderKind.LocalApplicationData] = @"C:\Users\Test\AppData\Local";
        fs.Folders[SpecialFolderKind.CommonApplicationData] = @"C:\ProgramData";
        fs.Folders[SpecialFolderKind.UserTemp] = @"C:\Users\Test\AppData\Local\Temp";
        fs.Folders[SpecialFolderKind.WindowsTemp] = @"C:\Windows\Temp";
        fs.AddDirectory(@"C:\Users\Test\AppData\Local\Temp");
        fs.AddDirectory(@"C:\Backups");
        return fs;
    }

    private sealed class InMemorySettingsStore(string backupDirectory) : IAppSettingsStore
    {
        public AppSettings Current { get; private set; } = new() { BackupDirectory = backupDirectory };

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
