param(
    [Parameter(Mandatory = $true)]
    [string]$NotesFile
)

$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$releaseRepository = 'LPRS1234/CodexUsageTray'
Push-Location -LiteralPath $projectDirectory
try {
    $originUrl = git remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $originUrl -ne "https://github.com/$releaseRepository.git") {
        throw '배포 저장소 origin이 예상한 GitHub 저장소와 다릅니다.'
    }

    $releaseInputs = @('src', 'assets', 'installer', 'tests', 'build.cmd', 'test.cmd', 'package.cmd', 'publish-release.ps1')
    $pendingChanges = git status --porcelain -- $releaseInputs
    if ($LASTEXITCODE -ne 0 -or $pendingChanges) {
        throw '배포에 필요한 소스 변경을 검토하고 커밋·푸시한 뒤 실행하세요. 이 스크립트는 자동으로 커밋하거나 푸시하지 않습니다.'
    }
    $branchName = git branch --show-current
    $commitId = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or -not $branchName) { throw '배포할 브랜치를 확인할 수 없습니다.' }
    $remoteHead = git ls-remote origin "refs/heads/$branchName"
    if ($LASTEXITCODE -ne 0 -or -not $remoteHead -or ($remoteHead -split '\s+')[0] -ne $commitId) {
        throw '현재 커밋이 origin에 푸시되어 있어야 합니다.'
    }
    $releaseNotesPath = (Resolve-Path -LiteralPath $NotesFile).Path

    & (Join-Path $projectDirectory 'package.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'Setup 빌드에 실패했습니다.' }
    & (Join-Path $projectDirectory 'test.cmd')
    if ($LASTEXITCODE -ne 0) { throw '검증에 실패하여 배포하지 않습니다.' }

    $setupPath = Join-Path $projectDirectory 'bin\setup\CodexUsageTray-Setup.exe'
    $checksumPath = $setupPath + '.sha256'
    $releaseVersion = [version]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($setupPath).FileVersion)
    $versionText = if ($releaseVersion.Revision -gt 0) { $releaseVersion.ToString(4) } else { $releaseVersion.ToString(3) }
    $releaseTag = 'v' + $versionText
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($checksumPath,
        $digest.Substring(7) + '  CodexUsageTray-Setup.exe' + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    # Upload and validate all assets while the release is a draft.
    gh release create $releaseTag $setupPath $checksumPath --repo $releaseRepository --target $commitId `
        --draft --title "Codex Usage Tray $versionText" --notes-file $releaseNotesPath
    if ($LASTEXITCODE -ne 0) { throw '초안 릴리스 업로드에 실패했습니다. 기존 릴리스를 덮어쓰지 않습니다.' }

    $releaseJson = gh api "repos/$releaseRepository/releases/tags/$releaseTag"
    if ($LASTEXITCODE -ne 0) { throw '초안 릴리스 검증 정보를 읽지 못했습니다.' }
    $releaseData = ($releaseJson -join [Environment]::NewLine) | ConvertFrom-Json
    $setupAsset = $releaseData.assets | Where-Object { $_.name -eq 'CodexUsageTray-Setup.exe' } | Select-Object -First 1
    if (-not $setupAsset -or $setupAsset.digest -ne $digest) {
        throw 'GitHub 설치 파일의 SHA-256 검증에 실패했습니다. 릴리스를 초안 상태로 남겼습니다.'
    }
    gh release edit $releaseTag --repo $releaseRepository --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw '정식 릴리스 공개에 실패했습니다. 초안 상태를 확인하세요.' }
    Write-Output "Published: https://github.com/$releaseRepository/releases/tag/$releaseTag"
}
finally {
    Pop-Location
}
