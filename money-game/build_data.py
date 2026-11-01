import json,urllib.request,time
def get(u):
    for t in range(5):
        try:
            return json.load(urllib.request.urlopen(u,timeout=40))[1]
        except Exception as e:
            print("retry",e);time.sleep(3)
    raise SystemExit("World Bank fail")
def g(i):
    u=f"https://api.worldbank.org/v2/country/all/indicator/{i}?format=json&per_page=400&date=2022"
    d=get(u);r={}
    for x in sorted(d,key=lambda x:x['date']):
        if x['value'] is not None:r[x['country']['id']]=x['value']
    return r
meta=get("https://api.worldbank.org/v2/country?format=json&per_page=400")
real={m['iso2Code'] for m in meta if m['region']['value']!='Aggregates'}
gdp,le,pop,br=[g(i) for i in("NY.GDP.PCAP.PP.CD","SP.DYN.LE00.IN","SP.POP.TOTL","SP.DYN.CBRT.IN")]
out=[[c,round(gdp[c]),round(le[c]),round(pop[c]*br[c]/1000)] for c in sorted(real) if all(c in x for x in(gdp,le,pop,br)) and len(c)==2 and c!='XK']
json.dump(out,open('data.json','w'),separators=(',',':'))
print(len(out),sum(x[3] for x in out),out[:2])
