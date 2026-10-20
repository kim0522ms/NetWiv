using NetWiv.Plugin.Fs;

internal static class FsWatchRootTests
{
    internal static Task MovementCannotBecomeCurrentAgain()
    {
        foreach (var ancestor in new[] { false, true })
        {
            var directory = Directory.CreateTempSubdirectory("netwiv-watch-movement-").FullName;
            try
            {
                var parent = Directory.CreateDirectory(Path.Combine(directory, "allowed")).FullName;
                var root = Directory.CreateDirectory(Path.Combine(parent, "root")).FullName;
                var outside = Directory.CreateDirectory(Path.Combine(directory, "outside")).FullName;
                using (var guard = WatchRoot.Open(root))
                {
                    Assert.True(guard.IsCurrent(), "A newly registered directory was rejected.");
                    // Ordinary activity inside the root must not invalidate its movement guard.
                    var ordinary = Path.Combine(root, "ordinary.txt");
                    File.WriteAllText(ordinary, "test"); File.Delete(ordinary);
                    Assert.True(guard.IsCurrent(), "Ordinary file activity invalidated the movement guard.");
                    var source = ancestor ? parent : root;
                    var moved = Path.Combine(outside, "moved");
                    if (OperatingSystem.IsWindows())
                    {
                        Assert.Throws<IOException>(() => Directory.Move(source, moved));
                        Assert.True(guard.IsCurrent(), "A blocked move invalidated the directory.");
                    }
                    else
                    {
                        Directory.Move(source, moved);
                        var secret = Path.Combine(ancestor ? Path.Combine(moved, "root") : moved, "outside-only.txt");
                        File.WriteAllText(secret, "test"); File.Delete(secret);
                        Directory.Move(moved, source);
                        // No callback or intermediate IsCurrent call occurs during the move cycle.
                        Assert.True(!guard.IsCurrent(), "A directory that left and returned was accepted.");
                        Assert.True(!guard.IsCurrent(), "Movement invalidation was reversible.");
                    }
                }
                // Releasing the verification handles must release Windows move restrictions too.
                var afterClose = Path.Combine(outside, "closed");
                Directory.Move(ancestor ? parent : root, afterClose);
            }
            finally { Directory.Delete(directory, true); }
        }
        return Task.CompletedTask;
    }

    internal static Task PhysicalAncestorsAndDisposal()
    {
        if (OperatingSystem.IsWindows()) return Task.CompletedTask;
        var directory = Directory.CreateTempSubdirectory("netwiv-watch-movement-").FullName;
        try
        {
            var parent = Directory.CreateDirectory(Path.Combine(directory, "physical")).FullName;
            var root = Directory.CreateDirectory(Path.Combine(parent, "root")).FullName;
            var link = Path.Combine(directory, "alias"); Directory.CreateSymbolicLink(link, root);
            using var guard = WatchRoot.Open(link);
            var moved = Path.Combine(directory, "moved");
            Directory.Move(parent, moved); Directory.Move(moved, parent);
            Assert.True(!guard.IsCurrent(), "A physical ancestor move through an alias was accepted.");
            guard.Dispose(); guard.Dispose();
            Assert.True(!guard.IsCurrent(), "A disposed directory guard was accepted.");
        }
        finally { Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }
}
