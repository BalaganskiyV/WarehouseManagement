"""HTTP integration checks against a real Kestrel process and an isolated SQLite database.
Usage: python integration.py --dotnet PATH_TO_DOTNET --evidence OUTPUT_DIR
Build the web project first. No third-party Python dependencies are required.
"""
from pathlib import Path
import argparse, base64, csv, hashlib, hmac, io, json, os, secrets, socket, sqlite3, subprocess, time
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

parser=argparse.ArgumentParser();parser.add_argument('--dotnet',required=True);parser.add_argument('--evidence',required=True)
args=parser.parse_args();root=Path(__file__).resolve().parents[1];out=Path(args.evidence).resolve();out.mkdir(parents=True,exist_ok=True)
dbfile=out/('integration-'+secrets.token_hex(4)+'.db')
sock=socket.socket();sock.bind(('127.0.0.1',0));port=sock.getsockname()[1];sock.close()
base=f'http://127.0.0.1:{port}'
admin_password=secrets.token_urlsafe(24);manager_password=secrets.token_urlsafe(24);clerk_password=secrets.token_urlsafe(24)
env=os.environ.copy();env.update({'ASPNETCORE_ENVIRONMENT':'Testing','ASPNETCORE_URLS':base,'Database__Provider':'Sqlite',
 'ConnectionStrings__Sqlite':f'Data Source={dbfile}','JwtSettings__SecretKey':secrets.token_urlsafe(48),
 'Bootstrap__AdminPassword':admin_password,'Bootstrap__ManagerPassword':manager_password,'Generator__Enabled':'false'})
results=[];captures={};proc=None;log=None
def check(name,condition,detail=''):
    results.append({'name':name,'passed':bool(condition),'detail':detail})
    print(('PASS ' if condition else 'FAIL ')+name,flush=True)
    if not condition: raise AssertionError(name+': '+detail)
def request(method,path,body=None,token=None):
    data=None if body is None else json.dumps(body,ensure_ascii=False).encode('utf8')
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    try:
        with urlopen(Request(base+path,data=data,headers=headers,method=method),timeout=45) as response:
            raw=response.read();status=response.status;ct=response.headers.get('Content-Type','')
    except HTTPError as e:raw=e.read();status=e.code;ct=e.headers.get('Content-Type','')
    if 'json' in ct:
        try: raw=json.loads(raw)
        except ValueError: pass
    return status,raw
def start(generator=False):
    global proc,log
    env['Generator__Enabled']=str(generator).lower();env['Generator__IntervalSeconds']='1'
    log=open(out/('generator-server.log' if generator else 'server.log'),'wb')
    proc=subprocess.Popen([str(Path(args.dotnet).resolve()),str(root/'WarehouseManagement/bin/Debug/net8.0/WarehouseManagement.dll')],cwd=root/'WarehouseManagement',env=env,stdout=log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
    for _ in range(120):
        if proc.poll() is not None:raise RuntimeError('Server stopped; inspect local server.log')
        try:
            if request('GET','/api/Products')[0]==401:return
        except (URLError,TimeoutError,ConnectionError):pass
        time.sleep(.25)
    raise RuntimeError('Server startup timed out')
def stop():
    global proc,log
    if proc is not None:
        proc.terminate()
        try:proc.wait(timeout=10)
        except subprocess.TimeoutExpired:proc.kill();proc.wait()
        proc=None
    if log:log.close();log=None
def login(name,password,extra=None):
    data={'username':name,'password':password};data.update(extra or {})
    status,body=request('POST','/api/Auth/login',data)
    check(f'Login {name}',status==200,str(status))
    return body['token'],body
try:
    start()
    check('Unauthenticated read returns 401',request('GET','/api/WarehouseEvents')[0]==401)
    check('Wrong password returns 401',request('POST','/api/Auth/login',{'username':'admin','password':'incorrect'})[0]==401)
    status,body=request('POST','/api/Auth/register',{'username':'clerk_test','password':clerk_password,'email':'clerk@example.invalid','role':'Admin'})
    check('Registration always assigns Clerk',status==201 and body['role']=='Clerk')
    check('Duplicate registration returns 409',request('POST','/api/Auth/register',{'username':'CLERK_TEST','password':clerk_password,'email':'other@example.invalid'})[0]==409)
    check('Weak password rejected',request('POST','/api/Auth/register',{'username':'weak','password':'123','email':'weak@example.invalid'})[0]==400)
    admin,_=login('admin',admin_password);manager,_=login('manager',manager_password);clerk,clerk_info=login('clerk_test',clerk_password,{'role':'Admin'})
    check('Malformed token rejected',request('GET','/api/WarehouseEvents',token='invalid.token.value')[0]==401)
    def signed_test_token(overrides):
        def encode(value):return base64.urlsafe_b64encode(json.dumps(value,separators=(',',':')).encode()).rstrip(b'=')
        payload={'iss':'WarehouseManagement','aud':'WarehouseManagement.Client','exp':int(time.time())+3600};payload.update(overrides)
        message=encode({'alg':'HS256','typ':'JWT'})+b'.'+encode(payload)
        signature=base64.urlsafe_b64encode(hmac.new(env['JwtSettings__SecretKey'].encode(),message,hashlib.sha256).digest()).rstrip(b'=')
        return (message+b'.'+signature).decode()
    check('Expired token rejected',request('GET','/api/WarehouseEvents',token=signed_test_token({'exp':int(time.time())-120}))[0]==401)
    check('Wrong audience rejected',request('GET','/api/WarehouseEvents',token=signed_test_token({'aud':'other-client'}))[0]==401)
    check('Injected login role cannot escalate access',clerk_info['role']=='Clerk' and request('DELETE','/api/WarehouseEvents/1',token=clerk)[0]==403)
    check('Clerk cannot access analytics or CSV',request('GET','/api/Analytics/summary',token=clerk)[0]==403 and request('GET','/api/WarehouseEvents/report/csv',token=clerk)[0]==403)
    status,products=request('GET','/api/Products',token=clerk);check('Product catalogue available',status==200 and len(products)>=1)
    product=products[0]
    input={'eventType':'Arrival','productId':product['id'],'quantity':10,'warehouseZone':'A-1','description':'Поставка; "особая"\nвторая строка','userId':999,'productName':'FORGED'}
    status,event=request('POST','/api/WarehouseEvents',input,clerk);check('Create valid event returns 201',status==201)
    eid=event['id'];captures['created_event']=event
    check('Author and product name taken from server',event['userId']!=999 and event['productName']==product['name'])
    status,read=request('GET',f'/api/WarehouseEvents/{eid}',token=clerk);check('Read event by ID',status==200 and read['quantity']==10)
    for name,changes in [('Unknown type',{'eventType':'string'}),('Negative quantity',{'quantity':-1}),('Zero arrival',{'quantity':0}),('Unknown product',{'productId':999999}),('Invalid zone',{'warehouseZone':'invalid'}),('Future date',{'eventDate':'2099-01-01T00:00:00Z'})]:
        data={**input,**changes};check(name+' rejected',request('POST','/api/WarehouseEvents',data,admin)[0]==400)
    check('Invalid date range rejected',request('GET','/api/WarehouseEvents/filter?start=2026-12-31&end=2026-01-01',token=admin)[0]==400)
    check('Unknown filter type rejected',request('GET','/api/WarehouseEvents/type/string',token=admin)[0]==400)
    status,summary=request('GET','/api/Analytics/summary',token=admin);check('Analytics total and quantity',status==200 and summary['totalEvents']==1 and summary['totalQuantity']==10)
    _,cached=request('GET','/api/Analytics/summary',token=admin);check('Analytics served from cache',cached==summary)
    update={**input,'quantity':21}
    check('Clerk update denied',request('PUT',f'/api/WarehouseEvents/{eid}',update,clerk)[0]==403)
    status,updated=request('PUT',f'/api/WarehouseEvents/{eid}',update,manager);check('Manager update succeeds',status==200 and updated['quantity']==21)
    check('Update retains original creator',updated['userId']==event['userId'])
    _,changed=request('GET','/api/Analytics/summary',token=admin);check('Update invalidates analytics cache',changed['totalQuantity']==21 and changed['generatedAtUtc']!=summary['generatedAtUtc'])
    check('Missing update returns 404',request('PUT','/api/WarehouseEvents/999999',update,admin)[0]==404)
    check('Manager deletion denied',request('DELETE',f'/api/WarehouseEvents/{eid}',token=manager)[0]==403)
    status,raw=request('GET','/api/WarehouseEvents/report/csv',token=manager)
    check('CSV UTF-8 BOM',status==200 and raw.startswith(b'\xef\xbb\xbf'))
    rows=list(csv.DictReader(io.StringIO(raw.decode('utf-8-sig')),delimiter=';'))
    check('CSV preserves Cyrillic quotes delimiters and newlines',len(rows)==1 and rows[0]['Description']==input['description'] and rows[0]['ProductName']==product['name'])
    formula={**input,'description':'=1+1','eventType':'Inventory','quantity':0}
    status,inventory=request('POST','/api/WarehouseEvents',formula,admin);check('Inventory permits zero quantity',status==201)
    status,raw=request('GET','/api/WarehouseEvents/report/csv?type=Inventory',token=admin)
    rows=list(csv.DictReader(io.StringIO(raw.decode('utf-8-sig')),delimiter=';'))
    check('CSV formula neutralization and filter',len(rows)==1 and rows[0]['Description']=="'=1+1")
    check('Admin deletes event',request('DELETE',f'/api/WarehouseEvents/{eid}',token=admin)[0]==204)
    check('Missing deletion returns 404',request('DELETE',f'/api/WarehouseEvents/{eid}',token=admin)[0]==404)
    _,summary=request('GET','/api/Analytics/summary',token=admin);check('Delete invalidates analytics cache',summary['totalEvents']==1 and summary['totalQuantity']==0)
    check('Manager generation denied',request('POST','/api/generate/events?count=1',token=manager)[0]==403)
    check('Generator limit enforced',request('POST','/api/generate/events?count=1001',token=admin)[0]==400)
    started=time.perf_counter();status,generation=request('POST','/api/generate/events?count=500',token=admin);elapsed=time.perf_counter()-started
    check('Batch generator creates exactly 500 events',status==200 and generation['created']==500,f'{elapsed:.3f} seconds');captures['batch_generation']={'count':500,'elapsedSeconds':round(elapsed,3)}
    _,summary=request('GET','/api/Analytics/summary',token=admin);check('Batch generation invalidates analytics',summary['totalEvents']==501);captures['analytics']=summary
    _,arrivals=request('GET','/api/WarehouseEvents/type/Arrival',token=clerk)
    _,filtered=request('GET','/api/Analytics/summary?type=Arrival',token=admin)
    check('Type filter agrees with analytics',len(arrivals)>0 and all(e['eventType']=='Arrival' for e in arrivals) and filtered['totalEvents']==len(arrivals))
    _,raw=request('GET','/api/WarehouseEvents/report/csv',token=admin);(out/'warehouse_events.csv').write_bytes(raw)
    with sqlite3.connect(dbfile) as db:
        check('No orphan references',db.execute('SELECT COUNT(*) FROM WarehouseEvents e LEFT JOIN Products p ON p.Id=e.ProductId LEFT JOIN Users u ON u.Id=e.UserId WHERE p.Id IS NULL OR u.Id IS NULL').fetchone()[0]==0)
        hashes=[r[0] for r in db.execute('SELECT PasswordHash FROM Users')]
        check('Passwords stored only as hashes',all(len(h)>30 and h not in [admin_password,manager_password,clerk_password] for h in hashes))
        check('Database foreign keys are configured',len(db.execute('PRAGMA foreign_key_list(WarehouseEvents)').fetchall())==2)
    status,html=request('GET','/');check('Dashboard is served',status==200 and 'Складские операции'.encode() in html)
    stop();start(True);admin,_=login('admin',admin_password)
    _,summary=request('GET','/api/Analytics/summary',token=admin);initial=summary['totalEvents']
    deadline=time.monotonic()+8
    while time.monotonic()<deadline:
        time.sleep(.4);_,after=request('GET','/api/Analytics/summary',token=admin)
        if after['totalEvents']>=initial+2:break
    check('Background generator updates cached analytics',after['totalEvents']>=initial+2)
finally:
    stop()
    (out/'results.json').write_text(json.dumps({'passed':sum(r['passed'] for r in results),'total':len(results),'checks':results,'evidence':captures,'databaseProvider':'SQLite','sqlServerRuntimeTested':False},ensure_ascii=False,indent=2),encoding='utf8')
    print(f"Results: {sum(r['passed'] for r in results)}/{len(results)}",flush=True)
