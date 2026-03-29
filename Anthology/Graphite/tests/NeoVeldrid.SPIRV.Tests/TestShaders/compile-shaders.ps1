$fileNames = Get-ChildItem -Path $PSScriptRoot -Recurse

foreach ($file in $fileNames)
{
    if ($file.Name.EndsWith("vert") -Or $file.Name.EndsWith("frag") -Or $file.Name.EndsWith("comp"))
    {
        Write-Host "Compiling $file"
        $fullInputPath = $file.FullName
        $fullOutputPath = $file.FullName + ".spv"
        glslangvalidator -V $fullInputPath -o $fullOutputPath
    }
}