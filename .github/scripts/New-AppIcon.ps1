param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath=(Resolve-Path -LiteralPath $Source).Path
$original=[System.Drawing.Bitmap]::new($sourcePath)
try {
  $left=$original.Width; $top=$original.Height; $right=-1; $bottom=-1
  for($y=0;$y -lt $original.Height;$y++){for($x=0;$x -lt $original.Width;$x++){if($original.GetPixel($x,$y).A -gt 8){$left=[Math]::Min($left,$x);$top=[Math]::Min($top,$y);$right=[Math]::Max($right,$x);$bottom=[Math]::Max($bottom,$y)}}}
  if($right -lt $left){throw 'Empty icon image'}
  $crop=[System.Drawing.Rectangle]::new($left,$top,$right-$left+1,$bottom-$top+1)
  $sizes=@(16,24,32,48,64,128,256)
  $images=[System.Collections.Generic.List[byte[]]]::new()
  foreach($size in $sizes){
    $bitmap=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try{
      $g=[System.Drawing.Graphics]::FromImage($bitmap)
      try{
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scale=($size-2.0)/[Math]::Max($crop.Width,$crop.Height)
        $w=[single]($crop.Width*$scale);$h=[single]($crop.Height*$scale)
        $rect=[System.Drawing.RectangleF]::new([single](($size-$w)/2),[single](($size-$h)/2),$w,$h)
        $g.DrawImage($original,$rect,$crop,[System.Drawing.GraphicsUnit]::Pixel)
      }finally{$g.Dispose()}
      $ms=[System.IO.MemoryStream]::new()
      try{$bitmap.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png);$images.Add($ms.ToArray())}finally{$ms.Dispose()}
    }finally{$bitmap.Dispose()}
  }
  $file=[System.IO.File]::Create([System.IO.Path]::GetFullPath($Destination))
  $writer=[System.IO.BinaryWriter]::new($file)
  try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++){
      $dim=[byte]($sizes[$i]%256);$writer.Write($dim);$writer.Write($dim);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$i].Length);$writer.Write([uint32]$offset);$offset+=$images[$i].Length
    }
    foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
  }finally{$writer.Dispose()}
}finally{$original.Dispose()}
