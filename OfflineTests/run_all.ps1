# Runs the three offline tests and writes output_<name>.txt next to this script.
$root = $PSScriptRoot
$repo = Split-Path $root -Parent
$commit = (git -C $repo rev-parse HEAD)
$sdk = (dotnet --version)
$jobs = @(
  @{ name = "rules"; args = @() },                                   # optical rules (Table 3.2), construction, descriptors, Carry1 rule
  @{ name = "gen";   args = @("140","20262016","3","200") },         # generator acceptance: runs, base seed, stride, max attempts
  @{ name = "bot";   args = @("140","20262016","3","200") }          # planner + independent plan checker
)
foreach ($j in $jobs) {
  $out = Join-Path $root ("output_" + $j.name + ".txt")
  @("Date run: " + (Get-Date -Format o), "Repo commit: " + $commit, "dotnet SDK: " + $sdk,
    "CodeVersion: MIDM-v3-Carry1-2026-10-21c", "Test: " + $j.name + "  args: " + ($j.args -join " "), "") | Set-Content $out
  Push-Location (Join-Path $root $j.name)
  dotnet run -c Release -- @($j.args) 2>&1 | Tee-Object -FilePath $out -Append
  Pop-Location
}
Write-Host "Done. Send output_rules.txt, output_gen.txt, output_bot.txt"
