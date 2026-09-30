from pathlib import Path
import argparse, json, subprocess, shutil, threading, functools
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from playwright.sync_api import sync_playwright
parser = argparse.ArgumentParser(description='Offline browser review of the synthetic comparison and catalogue-only exports.')
parser.add_argument('--documents', required=True, type=Path, help='Folder containing BuildStandard.html and ManualGuide.html exported by the engine')
parser.add_argument('--output', required=True, type=Path, help='Separate output folder for screenshots, PDFs and verification.json')
parser.add_argument('--chromium', help='Optional Chromium executable; otherwise use Playwright installed Chromium')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
out = args.output.resolve()
if out == args.documents.resolve():
 parser.error('Use a separate output folder to preserve input exports.')
out.mkdir(parents=True, exist_ok=True)
inputs=out/'inputs';inputs.mkdir(exist_ok=True)
shutil.copy(repo/'docs/ui/local-html-review.html',inputs/'workflow.html')
for name in ['BuildStandard','ManualGuide']:shutil.copy(args.documents/(name+'.html'),inputs/(name+'.html'))
class Quiet(SimpleHTTPRequestHandler):
 def log_message(self,*args):pass
server=ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(inputs)))
threading.Thread(target=server.serve_forever,daemon=True).start()
base='http://127.0.0.1:'+str(server.server_port)
checks=[]
def check(name,value):
 checks.append({'name':name,'passed':bool(value)})
 if not value:raise AssertionError(name)
requests=[];errors=[]
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path=args.chromium,headless=True,args=['--no-sandbox'])
 context=browser.new_context(permissions=['clipboard-read','clipboard-write'])
 page=context.new_page();page.on('pageerror',lambda e:errors.append(str(e)));page.on('request',lambda r:requests.append(r.url))
 page.goto(base+'/workflow.html')
 check('Manual candidates are not selectable',page.get_by_role('checkbox').count()==4 and page.get_by_role('checkbox',name='Include Device preparation',exact=True).is_disabled() and page.get_by_role('checkbox',name='Include Own-domain SPF bypass',exact=True).is_disabled())
 page.get_by_role('button',name='ENR-007 · Device preparation',exact=True).click()
 check('Inspecting a row does not select an action',not page.get_by_role('checkbox',name='Include Require MFA',exact=True).is_checked() and page.get_by_role('button',name='Review selected candidates',exact=True).is_disabled())
 page.get_by_role('checkbox',name='Include Require MFA',exact=True).check()
 page.locator('#area').select_option('Intune')
 check('Changing module clears previous ticks',page.get_by_role('button',name='Review selected candidates',exact=True).is_disabled())
 page.locator('#area').select_option('All areas')
 for name in ['Include Require MFA','Include UK time zone']:page.get_by_role('checkbox',name=name,exact=True).check()
 page.get_by_role('button',name='Review selected candidates',exact=True).click()
 check('Reviewed queue shows both selected inert candidates',page.locator('#queue tr').count()==2 and 'Create disabled' in page.locator('#queue').inner_text() and 'Create unassigned' in page.locator('#queue').inner_text())
 page.locator('#tenant').fill('wrong tenant')
 check('Wrong synthetic tenant confirmation refuses progression',page.locator('#simulate').is_disabled())
 page.locator('#tenant').fill('11111111-1111-1111-1111-111111111111')
 page.locator('#simulate').click()
 check('Acceptance configuration and functional results remain distinct','Accepted (synthetic)' in page.locator('#outcomes').inner_text() and 'Unknown' in page.locator('#outcomes').inner_text() and 'Pending' in page.locator('#outcomes').inner_text())
 page.get_by_role('button',name='CFG-WIN-011',exact=True).click()
 check('Missing after evidence does not look successful','do not replay' in page.locator('#after').inner_text())
 page.locator('#copy').click()
 page.wait_for_function("document.getElementById('copyStatus').textContent.length > 0")
 check('Copy reports successful synthetic output','copied' in page.locator('#copyStatus').inner_text())
 for size in [(1480,940),(1180,760),(940,660),(1180,640)]:
  page.set_viewport_size({'width':size[0],'height':size[1]})
  check(f'Prototype has no viewport overflow {size}',page.evaluate('document.documentElement.scrollWidth <= innerWidth'))
  page.screenshot(path=str(out/f'html-workflow-{size[0]}x{size[1]}.png'),full_page=True)
 page.pdf(path=str(out/'html-workflow.pdf'),format='A4',print_background=True)
 for name in ['BuildStandard','ManualGuide']:
  path=args.documents/(name+'.html');page.goto(base+'/'+name+'.html');page.set_viewport_size({'width':1180,'height':760})
  check(name+' displays the verified catalogue digest',json.loads((repo/'standards/manifest.json').read_text())['files']['2026.09.30.json'] in page.locator('body').inner_text())
  check(name+' contains all 93 control articles',page.locator('article[id]').count()==93)
  check(name+' historical retirements absent from contents',page.locator('article#ENR-003,article#ENR-004,article#SEC-WIN-002').count()==0)
  badanchors=page.evaluate("Array.from(document.querySelectorAll('a[href^=\"#\"]')).filter(a=>!document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1)))).map(a=>a.getAttribute('href'))")
  check(name+' all internal anchors resolve',not badanchors)
  check(name+' no horizontal viewport overflow',page.evaluate('document.documentElement.scrollWidth <= innerWidth'))
  page.screenshot(path=str(out/(name+'-screen.png')))
  page.pdf(path=str(out/(name+'.pdf')),format='A4',tagged=True,outline=True,print_background=True,margin={'top':'12mm','bottom':'12mm','left':'10mm','right':'10mm'})
  pdftext=subprocess.check_output(['pdftotext',str(out/(name+'.pdf')),'-'],text=True)
  check(name+' printed PDF contains all control IDs',all(c['id'] in pdftext for c in json.loads((repo/'standards/2026.09.30.json').read_text())['controls']))
 check('No JavaScript exceptions',not errors)
 check('Offline views made no external requests',all(u.startswith(base) for u in requests))
 browser.close()
server.shutdown()
server.server_close()
result={'checks':checks,'passed':sum(c['passed'] for c in checks),'failed':sum(not c['passed'] for c in checks),'javascriptErrors':errors,'externalRequests':[u for u in requests if not u.startswith(base)],'note':'Chromium local prototype and catalogue exports. No .NET browser host, authentication, physical DPI or live service acceptance.'}
(out/'verification.json').write_text(json.dumps(result,indent=2))
print(json.dumps({k:result[k] for k in ['passed','failed','javascriptErrors','externalRequests']}))
