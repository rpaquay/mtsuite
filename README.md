# mtsuite

**mtsuite** is a collection of multi-threaded command-line disk utilities for macOS, Linux, and Windows, designed for Solid State Drives (SSDs).

Each utility:

* **Works on many files at once**, using a thread count suited to the platform (see [Threads](#threads)).
* **Shows live progress** while it runs, optionally down to what each worker thread is doing.
* **Treats symbolic links and junction points as links**: it doesn't copy or delete what they point to (see [Links](#symbolic-links--junction-points)).
* **Supports long paths** (> 260 characters on Windows).

`mtcopy`, `mtmir`, and `mtcompact` also use **file cloning** (copy-on-write) where the file system supports it:

| Platform | File systems | API |
| --- | --- | --- |
| macOS | APFS | `clonefile` |
| Windows | ReFS, Dev Drive | `FSCTL_DUPLICATE_EXTENTS_TO_FILE` |
| Linux | not supported | |

Without cloning support, `mtcopy` and `mtmir` copy files normally, and `mtcompact` runs in simulation mode.

---

## Utilities

| Tool | What it does | Similar to |
| --- | --- | --- |
| `mtcopy` | Copies a directory tree, skipping files that are already up to date | `robocopy /S`, `rsync -rl` |
| `mtmir` | Mirrors a directory tree, and also deletes destination files that aren't in the source | `robocopy /MIR`, `rsync -a --delete` |
| `mtdel` | Deletes a directory tree | `rm -rf`, `rmdir /s /q` |
| `mtinfo` | Reports file, directory, and link counts and sizes for a directory tree | `du` |
| `mtfind` | Finds files by name pattern | `find -name` |
| `mtfindstr` | Finds text inside files | `grep -r`, `findstr /s` |
| `mtcompact` | Replaces identical files in the destination with clones of the source files, to reclaim disk space | |

### Examples

```bash
mtcopy ./source ./backup
```

```bash
mtmir ./source ./backup
```

`mtmir` deletes files from the destination. Add `--dry-run` (`-n`) to see what it would do first.

```bash
mtdel ./old-build
```

`mtdel` asks for confirmation. Add `-q` to skip the question, or `--dry-run` (`-n`) to see what it would delete without deleting anything.

```bash
mtinfo ./project --depth 3
```

```bash
mtfind ./project "*.cs"
```

The directory is optional and defaults to the current directory. You can also write the pattern as `--name "*.cs"`. Add `--include-dir` to match directory names too, and `--plain-output` to print only the matching paths.

```bash
mtfindstr ./project "TODO" --name "*.cs"
```

The directory comes first and is optional, then the text to search for. `--name` limits which files are searched.

```bash
mtcompact ./source ./copy --dry-run
```

`--dry-run` (`-n`) reports the space you could save without changing any files.

Run any tool with `--help` to see all of its options.

### Common options

| Option | Short | Description |
| --- | --- | --- |
| `--threads count` | `-t` | Number of concurrent threads, or `all` for one per CPU core |
| `--progress mode` | `-p` | `none`, `line`, `default`, or `full` (adds per-thread output). `--no-progress` (`-np`) is the same as `none` |
| `--warnings` | `-w` | Display warnings, such as entries that disappear during a search. Only `mtinfo`, `mtfind`, and `mtfindstr` report warnings for now |
| `--no-errors` | | Don't display errors. The exit code still reports them |
| `--gc` | | Display .NET garbage collector statistics |
| `--help` | `-h` | Display help |

Options start with `-` or `--` (or `/` on Windows), and take a value after a space or `=`, as in `--threads 4` or `--threads=4`. If an option is repeated, the last one wins. Arguments after `--` are positional, even if they start with `-`, as in `mtfindstr ./project -- "-foo"`.

Warnings and errors go to stderr, each printed once as it happens, and the final statistics show how many there were. Results and statistics go to stdout. Progress is only drawn on a terminal, so nothing extra ends up in a redirected stdout.

`mtcopy`, `mtmir`, `mtdel`, and `mtcompact` also have:

| Option | Short | Description |
| --- | --- | --- |
| `--dry-run` | `-n` | Show what would happen, without changing anything |

`mtcopy`, `mtmir`, and `mtcompact` also have:

| Option | Short | Description |
| --- | --- | --- |
| `--content` | `-fc` | Compare file contents. This is the default for `mtcompact` only |
| `--no-content` | `--ft` | Compare file modification times, which is faster. This is the default for `mtcopy` and `mtmir` |

`mtcopy` and `mtmir` also have:

| Option | Short | Description |
| --- | --- | --- |
| `--no-clone` | | Always copy, even where cloning is supported |

`mtinfo`, `mtfind`, and `mtfindstr` also have:

| Option | Short | Description |
| --- | --- | --- |
| `--follow-links` | `-L` | Follow links to directories. `mtinfo` then counts what they point to, so its totals can include the same files more than once |

`mtfind` and `mtfindstr` also have:

| Option | Short | Description |
| --- | --- | --- |
| `--plain-output` | `-po` | Print only the matches, with no progress. `--gc` statistics go to stderr |

`mtdel` also has:

| Option | Short | Description |
| --- | --- | --- |
| `--attributes value` | `-a` | Delete only files with these attributes, such as `R`, or `-R` for files that aren't read-only. Directories aren't deleted when you use this option. See `mtdel --help` |

### Threads

The default number of threads depends on the platform and the number of CPU cores:

| Platform | Default |
| --- | --- |
| macOS | 3/4 of the CPU cores, up to 12 |
| Linux | All CPU cores, up to 16 |
| Windows | All CPU cores, up to 16 |

The default is lower on macOS because adding threads stops helping on APFS, where they contend for kernel locks. `--help` shows the default for your machine.

---

## Symbolic Links & Junction Points

A symbolic link (or a junction point on Windows) points to another file or directory.

- **`mtcopy` / `mtmir`** create a new link with the same target instead of copying the target's contents.
- **`mtdel`** removes the link and leaves its target alone.
- **`mtinfo` / `mtfind` / `mtfindstr`** don't follow links unless you pass `--follow-links`. `mtinfo` counts each link either way. When following them, they detect link loops and skip them, and report loops and dangling links as warnings.

Because the target is copied as is, what a copied link points to depends on the kind of target:

- A **relative symbolic link** points inside the copy, if its target is part of the copied tree.
- An **absolute symbolic link** still points to the original location.
- A **junction point** always has an absolute target, so it still points to the original location.

### Example

Given the following directory:

```
test
├── foo
│   └── bar.link.txt (symbolic link -> "../bar.txt")
├── foo2
│   └── foo.link (symbolic link -> "../foo")
└── bar.txt (file)
```

Running `mtcopy test test-copy` results in:

```
test-copy
├── foo
│   └── bar.link.txt (symbolic link -> "../bar.txt")
├── foo2
│   └── foo.link (symbolic link -> "../foo")
└── bar.txt (file)
```

Both links in `test-copy` point to files in `test-copy`.

---

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build mtsuite.sln -c Release
```

```bash
dotnet test mtsuite.sln
```

To create standalone executables for every supported platform in `publish/`, run `publish-all.sh` (or `publish-all.ps1` on Windows):

```bash
./publish-all.sh
```

These executables are self-contained and don't use NativeAOT, so one machine can build them all. Build macOS arm64 executables on a Mac, because they need a code signature that .NET 8 only adds there. To build them on GitHub Actions instead, start **Build standalone** from the repository's **Actions** tab. It produces one zip file per platform.

### NativeAOT builds

NativeAOT executables are smaller and start faster. They can't be cross-compiled, so each platform is built on its own GitHub Actions runner.

The workflows only run when started by hand, from the repository's **Actions** tab: **Build all platforms**, **Build macOS**, **Build Linux**, and **Build Windows** (all labeled NativeAOT). Each one produces a zip file that you can download from the run's page.

These builds aren't signed or notarized by Apple, so macOS may block the tools after a browser download. If you trust the zip file, unzip it and run the script included in the macOS builds, from that folder in Terminal:

```bash
bash "mtsuite - run this to remove quarantine.sh"
```

### Creating a release

1. Set the new version in `src/core-filesystem/VersionNumber.cs`.
2. In [CHANGELOG.md](CHANGELOG.md), rename the "Unreleased" section to the new version and date.
3. Commit and push, then tag that commit and push the tag:

   ```bash
   git tag v1.2.3
   ```

   ```bash
   git push origin v1.2.3
   ```

4. In the repository's **Actions** tab, start both **Build all platforms (NativeAOT)** and **Build standalone**, and pick the tag under **Use workflow from**, so that the binaries match the tagged code. Download the zip files from each run's page when it finishes.
5. In the repository's **Releases** page, draft a new release from the tag, paste the version's changelog section as the description, and attach the zip files.

---

## Changes

See [CHANGELOG.md](CHANGELOG.md) for the changes in each version.

## License

[Apache License 2.0](LICENSE)
