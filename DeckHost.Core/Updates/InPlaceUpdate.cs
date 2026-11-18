using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DeckHost.Core.Updates;

/// <summary>What an update is doing, for the progress bar.</summary>
public enum UpdatePhase
{
    Unpacking,
    Installing,
    Done,
}

/// <summary>A step of an update: <paramref name="Total"/> 0 means the amount isn't known (an indeterminate bar).</summary>
public sealed record UpdateStep(UpdatePhase Phase, int Done, int Total)
{
    /// <summary>0 to 1, or -1 when the amount isn't known.</summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : -1;
}

/// <summary>
/// Installs an unpacked DeckHost over the one that is running. Running programs can't be
/// overwritten on Windows, but they can be renamed: every file the new version replaces is renamed
/// to <c>&lt;name&gt;.deckhost-old</c> first and the new one copied in its place (the same works on
/// Linux and macOS). The next start deletes the old files (<see cref="CleanUp"/>). A failure puts
/// everything back the way it was. Files the new version doesn't have (the uninstaller, anything
/// the user put there) are left alone.
/// </summary>
public static class InPlaceUpdate
{
    /// <summary>Added to a replaced file's name until the next start deletes it.</summary>
    public const string OldSuffix = ".deckhost-old";

    /// <summary>Installs <paramref name="newFolder"/>'s files into <paramref name="installFolder"/>; on failure nothing is changed.</summary>
    /// <exception cref="UpdateException">The update couldn't be installed (DeckHost is unchanged).</exception>
    public static void Apply(string newFolder, string installFolder, IProgress<UpdateStep>? progress = null, CancellationToken cancellationToken = default) =>
        Apply(newFolder, installFolder, progress, cancellationToken, static (source, target) => File.Copy(source, target, overwrite: false));

    internal static void Apply(string newFolder, string installFolder, IProgress<UpdateStep>? progress, CancellationToken cancellationToken, Action<string, string> copy)
    {
        ArgumentNullException.ThrowIfNull(newFolder);
        ArgumentNullException.ThrowIfNull(installFolder);
        if (!Directory.Exists(newFolder))
        {
            throw new UpdateException($"The new version isn't there: {newFolder}");
        }

        var entries = Walk(new DirectoryInfo(newFolder), string.Empty).ToList();
        var journal = new List<Change>();
        progress?.Report(new UpdateStep(UpdatePhase.Installing, 0, entries.Count));
        try
        {
            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (relative, source) = entries[index];
                var target = Path.Combine(installFolder, relative);
                CreateDirectories(Path.GetDirectoryName(target)!, journal);
                if (File.Exists(target) || IsLink(target))
                {
                    var aside = FreeAsideName(target);
                    File.Move(target, aside);
                    journal.Add(new Change(ChangeKind.MovedAside, target, aside));
                }

                if (source.LinkTarget is { } link)
                {
                    File.CreateSymbolicLink(target, link);
                }
                else
                {
                    copy(source.FullName, target);
                }

                journal.Add(new Change(ChangeKind.Added, target, null));
                progress?.Report(new UpdateStep(UpdatePhase.Installing, index + 1, entries.Count));
            }
        }
        catch (Exception ex)
        {
            Undo(journal);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new UpdateException($"The update couldn't be installed ({ex.Message}). DeckHost is unchanged.", ex);
        }

        progress?.Report(new UpdateStep(UpdatePhase.Done, entries.Count, entries.Count));
    }

    /// <summary>Added to the copy of the new version next to the install while <see cref="Swap"/> copies it.</summary>
    public const string NewSuffix = ".deckhost-new";

    /// <summary>
    /// Replaces the whole folder instead (for a signed macOS bundle, whose seal breaks with extra
    /// files in it): the new version is copied next to it (<see cref="NewSuffix"/>), then the install is
    /// renamed to <see cref="OldSuffix"/> and the copy renamed into its place. The folder's parent
    /// must be writable. On failure nothing is changed.
    /// </summary>
    /// <exception cref="UpdateException">The update couldn't be installed (DeckHost is unchanged).</exception>
    public static void Swap(string newFolder, string installFolder, IProgress<UpdateStep>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newFolder);
        ArgumentNullException.ThrowIfNull(installFolder);
        var target = Path.TrimEndingDirectorySeparator(installFolder);
        var incoming = target + NewSuffix;
        var aside = target + OldSuffix;
        try
        {
            DeleteDirectory(incoming);
            DeleteDirectory(aside);
            var entries = Walk(new DirectoryInfo(newFolder), string.Empty).ToList();
            progress?.Report(new UpdateStep(UpdatePhase.Installing, 0, entries.Count));
            Directory.CreateDirectory(incoming);
            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (relative, source) = entries[index];
                var path = Path.Combine(incoming, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (source.LinkTarget is { } link)
                {
                    File.CreateSymbolicLink(path, link);
                }
                else
                {
                    File.Copy(source.FullName, path);
                }

                progress?.Report(new UpdateStep(UpdatePhase.Installing, index + 1, entries.Count));
            }

            Directory.Move(target, aside);
            try
            {
                Directory.Move(incoming, target);
            }
            catch (Exception)
            {
                Directory.Move(aside, target);
                throw;
            }
        }
        catch (Exception ex)
        {
            try
            {
                DeleteDirectory(incoming);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new UpdateException($"The update couldn't be installed ({ex.Message}). DeckHost is unchanged.", ex);
        }

        progress?.Report(new UpdateStep(UpdatePhase.Done, 1, 1));
    }

    /// <summary>
    /// Deletes what an update replaced (still in use until DeckHost restarted): the renamed files in
    /// the folder and, after a <see cref="Swap"/>, the old folder next to it. Files still locked stay for the next time.
    /// </summary>
    /// <returns>How many files were deleted.</returns>
    public static int CleanUp(string installFolder)
    {
        ArgumentNullException.ThrowIfNull(installFolder);
        var target = Path.TrimEndingDirectorySeparator(installFolder);
        foreach (var leftover in new[] { target + OldSuffix, target + NewSuffix })
        {
            try
            {
                DeleteDirectory(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (!Directory.Exists(installFolder))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in OldFiles(new DirectoryInfo(installFolder)))
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    /// <summary>The new version's files (and links) with their paths relative to its folder. Linked folders are copied as links, not followed.</summary>
    private static IEnumerable<(string Relative, FileSystemInfo Source)> Walk(DirectoryInfo directory, string relative)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos().OrderBy(static entry => entry.Name, StringComparer.Ordinal))
        {
            var path = relative.Length == 0 ? entry.Name : Path.Combine(relative, entry.Name);
            if (entry is DirectoryInfo child && child.LinkTarget is null)
            {
                foreach (var item in Walk(child, path))
                {
                    yield return item;
                }
            }
            else
            {
                yield return (path, entry);
            }
        }
    }

    private static IEnumerable<FileInfo> OldFiles(DirectoryInfo directory)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo child)
            {
                if (child.LinkTarget is null)
                {
                    foreach (var file in OldFiles(child))
                    {
                        yield return file;
                    }
                }
            }
            else if (entry is FileInfo file && file.Name.EndsWith(OldSuffix, StringComparison.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    /// <summary>Deletes a folder (a link to one: only the link).</summary>
    private static void DeleteDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (directory.LinkTarget is not null)
        {
            directory.Delete();
        }
        else if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>The name to move a replaced file to; an older leftover that is still locked gets a numbered name instead.</summary>
    private static string FreeAsideName(string target)
    {
        var aside = target + OldSuffix;
        for (var number = 2; File.Exists(aside) || IsLink(aside); number++)
        {
            try
            {
                File.Delete(aside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                aside = $"{target}.{number}{OldSuffix}";
            }
        }

        return aside;
    }

    private static void CreateDirectories(string directory, List<Change> journal)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        CreateDirectories(Path.GetDirectoryName(directory)!, journal);
        Directory.CreateDirectory(directory);
        journal.Add(new Change(ChangeKind.CreatedDirectory, directory, null));
    }

    private static void Undo(List<Change> journal)
    {
        for (var index = journal.Count - 1; index >= 0; index--)
        {
            var change = journal[index];
            try
            {
                switch (change.Kind)
                {
                    case ChangeKind.Added:
                        File.Delete(change.Path);
                        break;
                    case ChangeKind.MovedAside:
                        File.Move(change.Aside!, change.Path);
                        break;
                    case ChangeKind.CreatedDirectory:
                        Directory.Delete(change.Path);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: the rest is still put back.
            }
        }
    }

    private enum ChangeKind
    {
        CreatedDirectory,
        MovedAside,
        Added,
    }

    private sealed record Change(ChangeKind Kind, string Path, string? Aside);
}
