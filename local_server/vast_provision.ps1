<# Provision, inspect, or destroy a Vast.ai GPU for GrokVideoStudio. #>
param(
    [ValidateSet('4090','A100','H100','Auto')][string]$Tier='Auto',
    [string]$VastApiKey='',
    [switch]$Teardown,
    [switch]$ListInstances,
    [switch]$Status
)

$ErrorActionPreference='Stop'
$script:VastCli=$null
$script:CurrentUser=$null
$InstanceFile=Join-Path $PSScriptRoot '.vast_instance_id'
$DiskGb=50
$RawBase='https://raw.githubusercontent.com/Common-joeAI/Grok-Video-Studio_WPF/main/local_server'

function Write-Step([string]$m){Write-Host "  $m"}
function Write-OK([string]$m){Write-Host "  $m" -ForegroundColor Green}
function Write-Warn([string]$m){Write-Host "  $m" -ForegroundColor Yellow}
function Write-Err([string]$m){Write-Host "  $m" -ForegroundColor Red}

function Resolve-VastCli([string]$Python=''){
    foreach($n in @('vastai','vast')){$c=Get-Command $n -ErrorAction SilentlyContinue;if($c){return $c.Source}}
    if($Python){
        $dirs=@()
        try{$p=& $Python -c "import sysconfig; print(sysconfig.get_path('scripts'))" 2>$null|Select-Object -Last 1;if($p){$dirs+=$p.Trim()}}catch{}
        try{$p=& $Python -m site --user-base 2>$null|Select-Object -Last 1;if($p){$dirs+=Join-Path $p.Trim() 'Scripts'}}catch{}
        foreach($d in ($dirs|Where-Object{$_}|Select-Object -Unique)){
            foreach($n in @('vastai.exe','vastai','vast.exe','vast')){$p=Join-Path $d $n;if(Test-Path $p){return $p}}
        }
    }
    return $null
}

function Find-Python{
    if(Get-Command py -ErrorAction SilentlyContinue){
        foreach($v in @('3.13','3.12','3.11','3.10','3.14')){
            try{$p=& py -$v -c 'import sys; print(sys.executable)' 2>$null|Select-Object -Last 1;if($p){$p=$p.Trim();if(Test-Path $p){return $p}}}catch{}
        }
    }
    $root=Join-Path $env:LOCALAPPDATA 'Programs\Python'
    if(Test-Path $root){foreach($d in (Get-ChildItem $root -Directory|Sort-Object Name -Descending)){$p=Join-Path $d.FullName 'python.exe';if(Test-Path $p){return $p}}}
    $c=Get-Command python -ErrorAction SilentlyContinue;if($c){return $c.Source};return $null
}

function Ensure-VastCli{
    Write-Step 'Checking for Vast.ai CLI...'
    $script:VastCli=Resolve-VastCli
    if($script:VastCli){Write-OK "Vast CLI found: $script:VastCli";return $true}
    $python=Find-Python
    if(-not $python){Write-Err 'Python 3.10 or newer was not found.';return $false}
    Write-Step "Installing official Vast.ai CLI with $python..."
    $o=& $python -m pip install --upgrade vastai 2>&1
    if($LASTEXITCODE -ne 0){$o=& $python -m pip install --user --upgrade vastai 2>&1}
    if($LASTEXITCODE -ne 0){Write-Err 'Could not install Vast.ai CLI.';$o|ForEach-Object{Write-Host "  $_" -ForegroundColor DarkGray};return $false}
    $script:VastCli=Resolve-VastCli $python
    if(-not $script:VastCli){Write-Err 'vastai installed, but vastai.exe could not be located.';return $false}
    Write-OK "Vast CLI installed: $script:VastCli";return $true
}

function Invoke-Vast([string[]]$Args){
    $o=& $script:VastCli @Args 2>&1;$code=$LASTEXITCODE;$text=($o-join "`n").Trim();$msg=$null
    if($text){try{$j=$text|ConvertFrom-Json;if($j.error -eq $true){$msg=if($j.msg){[string]$j.msg}else{$text}}}catch{}}
    if($code -ne 0 -or $msg){if(-not $msg){$msg=if($text){$text}else{'Unknown Vast.ai CLI error.'}};throw "vastai $($Args-join ' ') failed: $msg"}
    return $o
}

function Get-Value($Object,[string[]]$Names){
    if(-not $Object){return $null}
    foreach($n in $Names){$p=$Object.PSObject.Properties[$n];if($p -and $null -ne $p.Value -and "$($p.Value)" -ne ''){return $p.Value}}
    return $null
}

function Get-VastUser{
    $j=((Invoke-Vast @('show','user','--raw'))-join "`n")|ConvertFrom-Json
    if($j.PSObject.Properties['user']){return $j.user};return $j
}

function Set-VastKey([string]$Key){
    if([string]::IsNullOrWhiteSpace($Key)){return $false}
    $k=$Key.Trim()
    if($k -match '^(ghp_|github_pat_)'){Write-Err 'That is a GitHub token, not a Vast.ai API key.';return $false}
    if($k -notmatch '^[A-Fa-f0-9]{64}$'){Write-Err 'A Vast.ai API key must be exactly 64 hexadecimal characters.';return $false}
    $k=$k.ToLowerInvariant();$env:VAST_API_KEY=$k
    try{Invoke-Vast @('set','api-key',$k)|Out-Null;$script:CurrentUser=Get-VastUser}catch{Write-Err $_.Exception.Message;return $false}
    Write-OK 'Vast.ai API key verified.';return $true
}

function Ensure-ApiKey{
    if(-not [string]::IsNullOrWhiteSpace($VastApiKey)){return Set-VastKey $VastApiKey}
    if(-not [string]::IsNullOrWhiteSpace($env:VAST_API_KEY)){return Set-VastKey $env:VAST_API_KEY}
    try{$script:CurrentUser=Get-VastUser;Write-OK 'Existing Vast.ai authentication is valid.';return $true}catch{}
    Write-Warn 'No valid Vast.ai API key is configured.'
    $k=Read-Host '  Vast.ai API key';if(-not $k){return $false};return Set-VastKey $k
}

function Test-AccountCredit{
    try{if(-not $script:CurrentUser){$script:CurrentUser=Get-VastUser}}catch{Write-Err $_.Exception.Message;return $false}
    $email=Get-Value $script:CurrentUser @('email','normalized_email','username')
    $uid=Get-Value $script:CurrentUser @('id','user_id')
    $team=Get-Value $script:CurrentUser @('team_name','team_id')
    $identity=if($email){[string]$email}else{'unknown email'}
    if($uid){$identity+=" (user ID $uid)"};if($team){$identity+=" | team: $team"}
    Write-Step "Authenticated Vast.ai account: $identity"

    $raw=Get-Value $script:CurrentUser @('credit','balance','balance_available','current_balance')
    if($null -eq $raw){Write-Warn 'Vast.ai returned no credit/balance field; continuing without a local balance block.';return $true}
    try{$credit=[double]$raw}catch{Write-Warn "Could not parse Vast.ai credit '$raw'; continuing.";return $true}
    Write-Host ('  Vast.ai account credit: ${0:N2}' -f $credit) -ForegroundColor Cyan
    if($credit -le 0){
        Write-Err 'This API-key account reports no usable credit.'
        Write-Warn 'Confirm the email/user ID above matches the browser account or team that received the $50 payment.'
        return $false
    }
    return $true
}

function Get-Query{
    switch($Tier){
        '4090'{return "gpu_name=RTX_4090 num_gpus=1 reliability>=0.95 disk_space>=$DiskGb rented=false"}
        'A100'{return "gpu_name=A100 num_gpus=1 gpu_ram>=70 reliability>=0.95 disk_space>=$DiskGb rented=false"}
        'H100'{return "gpu_name=H100 num_gpus=1 gpu_ram>=70 reliability>=0.95 disk_space>=$DiskGb rented=false"}
        default{return "gpu_ram>=16 num_gpus=1 reliability>=0.95 dph<=0.50 disk_space>=$DiskGb rented=false"}
    }
}

function Search-Offers{
    Write-Step 'Searching for available GPU offers...'
    try{
        $j=((Invoke-Vast @('search','offers',(Get-Query),'--order','dph_total','--limit','5','--raw'))-join "`n")|ConvertFrom-Json
        $offers=if($j.PSObject.Properties['offers']){@($j.offers)}else{@($j)};$offers=@($offers|Where-Object{$_.id})
        if($offers.Count -eq 0){throw 'No matching offers were returned.'}
        Write-Host ''
        for($i=0;$i-lt $offers.Count;$i++){
            $o=$offers[$i];$raw=Get-Value $o @('dph_total','dph_base','dph','min_bid');$pt='price unavailable'
            if($null-ne $raw){try{$pt=('${0:N3}/hr' -f [double]$raw)}catch{}}
            Write-Host ('  [{0}] ID {1} | {2} | {3}' -f ($i+1),$o.id,$o.gpu_name,$pt)
        }
        if($Tier -eq 'Auto'){return [string]$offers[0].id}
        $c=Read-Host "  Pick an offer [1-$($offers.Count), Enter for 1]";if(-not $c){$c=1}
        $idx=[Math]::Max(0,[Math]::Min($offers.Count-1,([int]$c-1)));return [string]$offers[$idx].id
    }catch{Write-Err $_.Exception.Message;return $null}
}

function New-OnstartFile{
    $url="$RawBase/vast_bootstrap.sh"
    $s=@"
#!/bin/bash
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends ca-certificates curl
curl --fail --location --retry 5 --retry-delay 3 '$url' -o /tmp/gvs-bootstrap.sh
test -s /tmp/gvs-bootstrap.sh
chmod +x /tmp/gvs-bootstrap.sh
exec /tmp/gvs-bootstrap.sh
"@
    $bytes=[Text.Encoding]::UTF8.GetByteCount($s);if($bytes-gt 12000){throw "Startup script too large: $bytes bytes"}
    $p=Join-Path $env:TEMP ("gvs-vast-{0}.sh" -f [Guid]::NewGuid().ToString('N'));$s|Out-File $p -Encoding ascii -NoNewline
    Write-Step "Startup bootstrap request size: $bytes bytes";return $p
}

function Create-Instance([string]$OfferId){
    $onstart=$null
    try{
        $onstart=New-OnstartFile
        $j=((Invoke-Vast @('create','instance',$OfferId,'--image','nvidia/cuda:12.4.0-cudnn-runtime-ubuntu22.04','--disk',"$DiskGb",'--ssh','--env','-p 7860:7860/tcp','--onstart',$onstart,'--label','GrokVideoStudio','--cancel-unavail','--raw'))-join "`n")|ConvertFrom-Json
        $id=if($j.new_contract){$j.new_contract}elseif($j.id){$j.id}else{$null};if(-not $id){throw 'No instance ID was returned.'}
        [string]$id|Out-File $InstanceFile -Encoding ascii -NoNewline;Write-OK "Instance created: $id";return [string]$id
    }catch{
        $m=$_.Exception.Message
        if($m-match 'lacks credit|insufficient.*credit|balance'){Write-Err 'Vast.ai says this API-key account lacks usable credit.';Write-Warn 'Compare the authenticated account above with the account/team that received your payment.'}else{Write-Err $m}
        return $null
    }finally{if($onstart-and(Test-Path $onstart)){Remove-Item $onstart -Force -ErrorAction SilentlyContinue}}
}

function Get-Instance([string]$Id){
    $j=((Invoke-Vast @('show','instance',$Id,'--raw'))-join "`n")|ConvertFrom-Json
    if($j.PSObject.Properties['instances']){$a=@($j.instances);if($a.Count-gt 0){return $a[0]}};return $j
}

function Wait-ForUrl([string]$Id){
    Write-Step 'Waiting for the instance and public port...';$end=(Get-Date).AddMinutes(10)
    while((Get-Date)-lt $end){
        try{$i=Get-Instance $Id;$ip=[string]$i.public_ipaddr;$port=$null
            if($i.ports){$m=$i.ports.PSObject.Properties['7860/tcp'];if($m-and$m.Value){$a=@($m.Value);if($a.Count-gt 0){$port=[string]$a[0].HostPort}}}
            if($ip-and$port){return "http://${ip}:${port}"}
        }catch{}
        Write-Host '.' -NoNewline -ForegroundColor DarkGray;Start-Sleep 5
    }
    Write-Host '';return $null
}

function Wait-ForHealth([string]$Url){
    Write-Step 'Waiting for the LTX-Video server to finish installing...';$end=(Get-Date).AddMinutes(20)
    while((Get-Date)-lt $end){try{$h=Invoke-RestMethod "$($Url.TrimEnd('/'))/health" -TimeoutSec 10;if($h.status-eq'healthy'){Write-OK "Server healthy. GPU: $($h.gpu_available); VRAM: $($h.total_vram_gb) GB";return}}catch{};Write-Host '.' -NoNewline -ForegroundColor DarkGray;Start-Sleep 10}
    Write-Host '';Write-Warn 'Server is not healthy yet; the instance is active and billing.';Write-Warn 'Bootstrap log: /var/log/grok-video-studio-bootstrap.log'
}

function Destroy-SavedInstance{
    if(-not(Test-Path $InstanceFile)){Write-Warn 'No saved instance ID found.';return}
    $id=(Get-Content $InstanceFile -Raw).Trim();Invoke-Vast @('destroy','instance',$id,'--raw')|Out-Null;Remove-Item $InstanceFile -Force;Write-OK "Instance $id destroyed. Billing stopped."
}

Write-Host "`n==> GrokVideoStudio -> Vast.ai Cloud GPU`n" -ForegroundColor Cyan
if(-not(Ensure-VastCli)){exit 1};if(-not(Ensure-ApiKey)){exit 1}
if($ListInstances){Invoke-Vast @('show','instances')|ForEach-Object{Write-Host $_};exit 0}
if($Status){if(Test-Path $InstanceFile){Invoke-Vast @('show','instance',(Get-Content $InstanceFile -Raw).Trim())|ForEach-Object{Write-Host $_}}else{Write-Warn 'No saved instance ID found.'};exit 0}
if($Teardown){Destroy-SavedInstance;exit 0}
if(-not(Test-AccountCredit)){exit 1}
if($Tier-eq'Auto'){Write-Host "  [1] RTX 4090`n  [2] A100`n  [3] H100`n  [4] Auto";switch(Read-Host '  Pick a tier [1-4, Enter for 1]'){'2'{$Tier='A100'}'3'{$Tier='H100'}'4'{$Tier='Auto'}default{$Tier='4090'}}}
Write-Host "  Selected tier: $Tier" -ForegroundColor Cyan
$offer=Search-Offers;if(-not $offer){exit 1};$id=Create-Instance $offer;if(-not $id){exit 1};$url=Wait-ForUrl $id
if(-not $url){Write-Err "Could not resolve URL. Instance $id is active and billing.";Write-Warn 'Run with -Teardown to stop it.';exit 1}
Write-Host "`n  SERVER URL: $url`n" -ForegroundColor Green;Write-Host '  Paste this into Settings -> Local Server URL.';Write-Host "  Stop billing with: .\vast_provision.ps1 -Teardown`n";Wait-ForHealth $url;Write-Host 'Done.' -ForegroundColor Green
