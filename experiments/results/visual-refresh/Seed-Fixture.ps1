$ErrorActionPreference = 'Stop'
$dir = (Resolve-Path 'experiments/JoJot.Avalonia.Tests/bin/Release/net11.0/win-x64').Path
foreach ($name in 'SQLitePCLRaw.core.dll','SQLitePCLRaw.provider.e_sqlite3.dll','SQLitePCLRaw.batteries_v2.dll','Microsoft.Data.Sqlite.dll') { [Reflection.Assembly]::LoadFrom((Join-Path $dir $name)) | Out-Null }
[System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $dir 'e_sqlite3.dll')) | Out-Null
[SQLitePCL.Batteries_V2]::Init()
$root=New-Item -ItemType Directory .local/avalonia-visual-refresh/fixture
Copy-Item .local/avalonia-ux/final/fixture/jojot.db $root.FullName
$db=[Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$($root.FullName)/jojot.db")
$db.Open()
$cmd=$db.CreateCommand()
$cmd.CommandText="DELETE FROM notes; DELETE FROM app_state; DELETE FROM preferences; DELETE FROM pending_moves; INSERT INTO app_state(desktop_guid,desktop_name) VALUES('default','Writing');"
$cmd.ExecuteNonQuery() | Out-Null
$names=@('A little room to think','Ideas worth keeping','Friday, slower','Reading list','Weekend plans','Quick capture')
$body=@'
A little room to think

Keep the useful things close.

This is a quiet place for unfinished thoughts, small plans,
and the note you do not want to lose.

TODAY

  Sketch the first version.
  Leave room for a better idea.
  Take a walk before the next meeting.

A note for later

Simple tools should make it easier to begin.
The rest can wait.
'@
for($i=0;$i -lt $names.Count;$i++) {
 $cmd=$db.CreateCommand()
 $cmd.CommandText="INSERT INTO notes(desktop_guid,name,content,pinned,sort_order,created_at,updated_at) VALUES('default',@name,@content,@pin,@sort,'2026-11-12 09:00:00','2026-11-13 09:00:00')"
 $cmd.Parameters.AddWithValue('@name',$names[$i]) | Out-Null
 $cmd.Parameters.AddWithValue('@content',$(if($i -eq 0){$body}else{"A few notes about $($names[$i].ToLowerInvariant())."})) | Out-Null
 $cmd.Parameters.AddWithValue('@pin',[int]($i -lt 2)) | Out-Null
 $cmd.Parameters.AddWithValue('@sort',$i) | Out-Null
 $cmd.ExecuteNonQuery() | Out-Null
}
$db.Close()
