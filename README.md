#mtsuite

mtsuite is a suite of a few disk utilities that have 3 things in common:

* They support long path (path > 260 characters)
* They are multi-threaded, making them vastly faster on multi-core machines with SSD
* They work with directories containing (Windows) Symbolic Links.

The 4 programs included are

* mtdel: delete a directory recursively
* mtcopy: copy a source directory recursively to a destination directory
* mtmir: same as mtcopy, except extra files not present in the source are deleted from the destination
* mtinfo: display file system statistics of a directory (# of files, # of subdirectories, size, etc.)
