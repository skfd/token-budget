# Clean script for LLM Token Widget - removes bin and obj folders

Write-Host "`n=== Cleaning bin and obj folders ===" -ForegroundColor Cyan

# Define common folder names to clean
$foldersToClean = @("bin", "obj")

# Search for and remove bin and obj folders recursively
foreach ($folderName in $foldersToClean) {
    Write-Host "Searching for '$folderName' folders..." -ForegroundColor Yellow
    
    # Find all folders with the specified name - use . instead of -Name to get full objects
    $folders = Get-ChildItem -Path "." -Filter $folderName -Recurse -Directory -ErrorAction SilentlyContinue
    
    if ($folders.Count -eq 0) {
        Write-Host "  No '$folderName' folders found." -ForegroundColor Green
    } else {
        foreach ($folder in $folders) {
            $fullPath = $folder.FullName
            Write-Host "  Removing: $fullPath" -ForegroundColor Red
            
            try {
                Remove-Item -Path $fullPath -Recurse -Force -ErrorAction Stop
                Write-Host "    Removed successfully." -ForegroundColor Green
            } catch {
                Write-Host "    Failed to remove: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
}

Write-Host "`nCleaning completed!" -ForegroundColor Green