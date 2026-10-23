# Local Simple File Browser changes

This embedded package preserves Simple File Browser 1.7.5 from
https://github.com/yasirkula/UnitySimpleFileBrowser at revision
`e24fa529103603043f6dad1b66192ff72954ef6e`, including its license and Unity metadata.

`FileBrowser.ShowLoadDialog` has an optional trailing `bool filesFirst = false`.
For file-only dialogs opting in, files precede directories. Both groups retain
the original alphabetical name comparison. Every load/save dialog request sets
this instance option before changing the path or displaying entries, so the
option cannot leak into later dialogs. Folder and mixed-mode dialogs ignore it.
The existing `sortFilesByName` setting is unchanged.

YARG opts in only through `FileExplorerHelper.OpenChooseSourceDtaFile`; ordinary
file, save, and folder pickers keep their original ordering and filter behavior.

Keep these changes when updating the dependency. The package is local and does
not require publishing or fetching a fork.
