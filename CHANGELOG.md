# Changelog

All notable changes to mtsuite, newest first. Entries before 0.10.0 were written later from the commit history, so they are less detailed. There were no versions 0.9.4 and 0.9.20.

## 0.10.0 (2026-11-01)

### Changed

These can affect scripts:

- Warnings and errors, including missing paths, go to stderr. Invalid arguments send the banner, the errors and the usage to stderr.
- Each error is printed once, as it happens, and is no longer repeated after the statistics.
- `mtfind` and `mtfindstr` no longer follow symbolic links by default. Use `--follow-links` to follow them.
- `mtfind` and `mtfindstr --plain-output` no longer draw progress, print matches only once, and send `--gc` statistics to stderr.
- `mtdel --attributes` with only excluded attributes, such as `-R`, now matches files without those attributes. It used to match nothing.
- `--ft` is now the same as `--no-content` in `mtcopy`, `mtmir` and `mtcompact`.
- `mtinfo --longestpath` is now `--longest-path`. The old name still works.
- The default thread count on macOS is 3/4 of the CPU cores, up to 12.

### Added

- `--warnings` and `--no-errors` on all tools, and a warning count in live progress and final statistics. For now, only `mtinfo`, `mtfind` and `mtfindstr` report warnings.
- `--dry-run` on `mtcopy`, `mtmir` and `mtdel`.
- `--attributes` as the long name of `mtdel -a`.
- `mtfind [directory] pattern`, in addition to `--name`.
- `--option=value`, option values starting with `-`, and `--` to end the options.
- Manual GitHub Actions workflows for NativeAOT builds.

### Fixed

- `--follow-links` no longer walks forever on link loops. Loops are skipped and reported as warnings.
- `mtcopy` and `mtmir` now copy real NTFS junction points.
- `mtmir` no longer fails on a directory link whose target was deleted during the run.
- Parser crashes on `--` and on repeated options. The last repeated option now wins.
- The `mtfindstr` banner showed the name of `mtfind`.

### Performance

- Directory enumeration on macOS uses `getattrlistbulk`: `mtinfo` on a tree of 1.3 million entries went from 7.5s to 5.1s.
- Directory enumeration on Linux uses `getdents64` and `statx`.
- `mtfindstr` is about 11x faster on large text files, and searches the files of a directory concurrently.

## 0.9.28 (2026-08-18)

- Support file cloning on Windows ReFS.

## 0.9.27 (2026-08-14)

- Show GC statistics in the progress output.

## 0.9.26 (2026-08-13)

- Add an option for single line progress.
- Use the same parameter names across tools.

## 0.9.25 (2026-08-12)

- Make `mtfind` arguments closer to Unix `find`.
- Use a space to separate options from their values.
- Make `mtfindstr` faster and reduce its allocations.
- Delete directory entries in the Linux and macOS native way.
- Improve the statistics output.

## 0.9.24 (2026-08-08)

- Make output more consistent across tools.

## 0.9.23 (2026-08-08)

- Print errors as they happen.
- Display `mtfind` and `mtfindstr` results as soon as they are found.
- Show thread status in all tools.
- Fix clone support being detected too early.

## 0.9.22 (2026-08-07)

- Fix deleting symbolic links to directories.
- Fix the detection of files that are already cloned.
- Fix threads showing as idle while checking for clones.
- Say "clone" rather than "copy" where it applies.

## 0.9.21 (2026-08-07)

- Detect files that are already cloned.
- Fix a macOS arm64 bug.

## 0.9.19 (2026-08-07)

- Publish with ReadyToRun and without compression for faster startup.

## 0.9.18 (2026-08-07)

- Fix `-fc`, which did not compare contents when modification dates were equal.
- Show progress while comparing file contents.
- Detect ANSI support automatically.
- Remove `\\?\` path support, which .NET 8 no longer needs.
- Use the same statistics display across tools.

## 0.9.17 (2026-08-06)

- Add `mtcompact`, which clones files common to the source and destination, with a dry run mode.
- Use `clonefile` on macOS when available.
- Show per-thread progress.
- Copy large files asynchronously.
- Default to the lower of the CPU core count and 16 threads.
- Fix display corruption when the terminal wraps lines.

## 0.9.16 (2026-08-05)

- Internal changes only.

## 0.9.15 (2026-08-05)

- Performance and memory improvements.
- Remove Windows-specific code from the file system layer.

## 0.9.14 (2026-08-03)

- Use `--` and `-` for command line options.
- Compare file names according to each OS, with semantics comparable to `rsync -a`.
- Add a script that publishes the binaries for all platforms.

## 0.9.13 (2026-03-15)

- Move to portable .NET, so the tools can run beyond Windows.
- Add `mtfindstr`, first called `mtgrep`, with line and column numbers, and skipping of likely binary files.
- Add `--include-dir` to `mtfind`.
- Record and display warnings.
- Use ANSI sequences for progress.
- Support multiple file name patterns in `mtfindstr`.

## 0.9.12 (2019-09-06)

- Enumerate directories with `NtQueryDirectoryFile`.
- Reduce memory usage and improve performance.

## 0.9.11 (2019-08-30)

- Add `mtfind`, with wildcard patterns, plain output and following of symbolic links.
- Ignore "path not found" errors while searching.

## 0.9.10 (2019-08-30)

- No recorded changes.

## 0.9.9 (2018-10-22)

- Finish comparing files by contents.
- Create symbolic links without elevation where Windows allows it.

## 0.9.8 (2018-07-30)

- Start comparing files by contents.
- Batch multi-line console output.

## 0.9.6 and 0.9.7 (2018-06-18)

- Use multi-line console output for progress.
- Support cancellation during file copy.
- Add support for forcing long path syntax.
- Fix the CPU usage figure.
- Move the file system classes into a separate assembly.

## 0.9.5 (2016-09-23)

- Improve performance with symbolic links and junction points.
- Allow `mtinfo /lp` to choose the number of paths to collect.
- Copy files more efficiently when the destination does not exist.
- Display a stack trace for unexpected errors.
- Fix an intermittent access violation when copying symbolic links, and handling of errors during copy.
- Fix parsing of attribute filters.

## 0.9.3 (2016-08-24)

- Fix an invalid memory access error.

## 0.9.2 (2016-08-04)

- Update links whose target differs during copy.
- Handle deleting SYSTEM files in the destination.

## 0.9.1 (2016-03-21)

- Add support for creating junction points.

## 0.9.0 (2015-12-14)

- Initial version.
