param([Parameter(Mandatory)] [string] $RunDirectory, [Parameter(Mandatory)] [string] $OutputCsv)
$ErrorActionPreference='Stop'
function Percentile($values, [double]$fraction) {
    $sorted=@($values | Sort-Object)
    if ($fraction -eq 0.5 -and $sorted.Count % 2 -eq 0) { return ($sorted[$sorted.Count/2-1]+$sorted[$sorted.Count/2])/2 }
    return $sorted[[int][Math]::Ceiling($sorted.Count*$fraction)-1]
}
$rows=foreach ($file in (Get-ChildItem (Join-Path $RunDirectory 'runs/measured') -Recurse -Filter '*.scroll.json')) {
    $data=Get-Content $file.FullName -Raw | ConvertFrom-Json
    foreach ($cell in $data) {
        [pscustomobject]@{Round=$file.Directory.Parent.Name; Variant=$file.Directory.Name;
            Wrap=$cell.Wrap; Highlights=$cell.Highlights; CpuMs=$cell.CpuMs; ElapsedMs=$cell.ElapsedMs;
            Frames=$cell.FrameMs.Count; FrameMedianMs=(Percentile $cell.FrameMs 0.5); FrameP95Ms=(Percentile $cell.FrameMs 0.95);
            InputMedianMs=(Percentile $cell.InputMs 0.5); InputP95Ms=(Percentile $cell.InputMs 0.95);
            LayoutChanges=$cell.TextLayoutChanges; LayoutEvents=$cell.LayoutEvents;
            HighlightRenders=$cell.HighlightRenderCount; Rectangles=$cell.HighlightRectangles;
            HighlightRenderMs=$cell.HighlightRenderMs; HighlightAllocatedBytes=$cell.HighlightAllocatedBytes;
            BytesPerHighlightRender=$(if ($cell.HighlightRenderCount) {$cell.HighlightAllocatedBytes/$cell.HighlightRenderCount} else {0});
            ViewportWidth=$cell.ViewportWidth; ViewportHeight=$cell.ViewportHeight; ExtentHeight=$cell.ExtentHeight;
            InputSteps=$cell.ScrollY.Count; MinimumY=($cell.ScrollY | Measure-Object -Minimum).Minimum; MaximumY=($cell.ScrollY | Measure-Object -Maximum).Maximum}
    }
}
$rows | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation
$rows | Group-Object Variant,Wrap,Highlights | ForEach-Object {
    [pscustomobject]@{Cell=$_.Name; Runs=$_.Count; CpuMedian=(Percentile $_.Group.CpuMs 0.5);
        FrameMedian=(Percentile $_.Group.FrameMedianMs 0.5); HighlightMs=(Percentile $_.Group.HighlightRenderMs 0.5);
        HighlightMB=(Percentile $_.Group.HighlightAllocatedBytes 0.5)/1MB}
} | Format-Table
