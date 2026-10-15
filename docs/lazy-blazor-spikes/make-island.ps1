# Research spike: turn a published Showroom wwwroot into "island" pages. Each page is a static scaffold plus a loader that starts the Blazor
# runtime on demand (autostart=false, Blazor.start(), then Blazor.rootComponents.add for a component registered with RegisterForJavaScript).
# Usage: .\make-island.ps1 <wwwroot> <pageFile> <mountJsExpression>   e.g.  .\make-island.ps1 $w p-prism.html "await add('prism',{})"
param([string]$Wwwroot, [string]$File, [string]$MountExpr)
$t0 = [IO.File]::ReadAllText("$Wwwroot\index.html")
$a = $t0.IndexOf('<div id="app">'); $b = $t0.IndexOf('<div id="blazor-error-ui">')
$c = $t0.IndexOf('<script src="_framework/blazor.webassembly'); $d = $t0.LastIndexOf('</body>')
$name = (Get-ChildItem "$Wwwroot\_framework" -Filter 'blazor.webassembly.*.js' | select -first 1).Name
$scaf = '<main id="scaffold" style="max-width:720px;margin:24px auto;padding:0 16px"><h1>island</h1><div id="slot"></div></main>'
$loader = @"
<script>
 window.__t={nav:0}; var started=null;
 function startRuntime(){ if(started) return started; started=new Promise(function(resolve,reject){ var s=document.createElement('script'); s.src='_framework/$name'; s.setAttribute('autostart','false');
  s.onload=function(){ Blazor.start({loadBootResource:function(type,name){ var fw=window.EA_FRAMEWORK_BASE; if(!fw) return undefined; var file=name; if(name==='dotnet.js'){ var m=JSON.parse(document.querySelector('script[type="importmap"]').textContent).imports['./_framework/dotnet.js']; if(m) file=m.substring(m.lastIndexOf('/')+1);} return fw+file; }}).then(function(){window.__t.runtimeStarted=performance.now();resolve();},reject); };
  document.head.appendChild(s); }); return started; }
 async function add(id,params){ for(var i=0;;i++){ try{ await Blazor.rootComponents.add(document.getElementById('slot'),id,params); window.__t.addAttempts=i+1; return; }catch(e){ if(i>100) throw e; await new Promise(function(r){setTimeout(r,50);}); } } }
 (async function(){ await startRuntime(); window.__t.beforeMount=performance.now(); $MountExpr; window.__t.mounted=performance.now(); })();
</script>
"@
$t = $t0.Substring(0, $a) + $scaf + "`r`n" + $t0.Substring($b, $c - $b) + $loader + "`r`n" + $t0.Substring($d)
[IO.File]::WriteAllText("$Wwwroot\$File", $t, (New-Object Text.UTF8Encoding($false)))
