-- Observe full scans initiated by the player in Aux or OctoAuctionator.
local B=TwilightHammerSync_Bridge
local function capturePage(scan)
 local count,total=GetNumAuctionItems('list')
 if not count or not total or count~=math.min(50,math.max(0,total-scan.pages*50)) then
  scan.invalid=true
  return
 end
 scan.pages=scan.pages+1
 scan.expectedPages=math.max(1,math.ceil(total/50))
 for index=1,count do
  local name,texture,quantity,quality,_,_,bid,_,buyout,_,_,seller=GetAuctionItemInfo('list',index)
  local link=GetAuctionItemLink('list',index)
  local _,_,id=string.find(link or '','item:(%d+)')
  id=tonumber(id)
  if not id or not name or name=='' or not quantity or quantity<1 then
   scan.invalid=true
   return
  end
  local row=scan.items[id]
  if not row then
   local _,_,_,level,class=GetItemInfo('item:'..id..':0:0:0')
   local icon=string.lower(texture or '')
   icon=string.gsub(icon,'\\','/')
   icon=string.gsub(icon,'^.*/','')
   icon=string.gsub(icon,'%.%a+$','')
   if not string.find(icon,'^[a-z0-9_%-]+$') then icon='' end
   row={itemId=id,name=name,quality=math.max(0,quality or 1),icon=icon,itemLevel=level or 0,itemClass=class or '',quantity=0,auctionCount=0,totalBuyout=0,listings={},prices={},sellers={}}
   scan.items[id]=row
  end
  row.quantity=row.quantity+quantity
  row.auctionCount=row.auctionCount+1
  row.totalBuyout=row.totalBuyout+(buyout or 0)
  if buyout and buyout>0 then table.insert(row.prices,math.floor(buyout/quantity)) end
  if seller and seller~='' then row.sellers[seller]=true end
  table.insert(row.listings,{name=name,count=quantity,buyout=buyout or 0,startingBid=bid or 0,seller=seller or ''})
 end
end

local function finish(scan,addon,label)
 if scan.invalid or scan.pages~=scan.expectedPages then
  B.say(label..' scan was incomplete. Previous website scan preserved.')
  return
 end
 local items={}
 for _,row in pairs(scan.items) do
  table.sort(row.prices)
  local count=table.getn(row.prices)
  if count>0 then
   row.minBuyout=row.prices[1]
   row.maxBuyout=row.prices[count]
   row.medianBuyout=math.floor((row.prices[math.floor((count+1)/2)]+row.prices[math.ceil((count+1)/2)])/2)
  end
  row.sellerCount=0
  for _ in pairs(row.sellers) do row.sellerCount=row.sellerCount+1 end
  row.prices=nil
  row.sellers=nil
  table.insert(items,row)
 end
 local snapshot=B.base('auctions',items)
 snapshot.source=label..' Full Scan '..(GetAddOnMetadata(addon,'Version') or '')
 if table.getn(items)>6000 or string.len(B.json(snapshot))>890000 then
  B.say(label..' scan exceeds the website upload limit. Previous scan preserved.')
  return
 end
 if B.save('auctions',snapshot) then
  B.say(label..' Full Scan saved for automatic website upload.')
 else
  B.say(label..' Full Scan captured. Log out or /reload to save it for upload.')
 end
end

local function newScan()
 return {items={},pages=0}
end

local function observePage(scan)
 if not pcall(capturePage,scan) then scan.invalid=true end
end

local installedAux,installedAuctionator
local function installAux()
 if installedAux or not IsAddOnLoaded('aux-addon') or type(require)~='function' then return end
 local api=require('aux.core.scan')
 if type(api.start)~='function' then return end
 -- Aux exposes a read-only interface. Its module export table updates all callers.
 local original=api.start
 local exports=getfenv(original).M
 if not exports then return end
 exports.start=function(params)
  local queries=params.queries
  local query=queries and queries[1]
  local server=query and query.blizzard_query
  local filter=query and string.gsub(query.prettified or '?','|c%x%x%x%x%x%x%x%x','')
  if filter then filter=string.gsub(filter,'|r','') end
  if not B.realmOK() or params.type~='list' or table.getn(queries or {})~=1 or
     not server or filter~='<>' then
   return original(params)
  end
  for key,value in pairs(server) do
   if value~=nil and value~=false and value~='' and not (key=='first_page' and value==0) then
    return original(params)
   end
  end
  local scan=newScan()
  local onPage,onComplete=params.on_page_loaded,params.on_complete
  params.on_page_loaded=function(page,pages,lastPage)
   if page==scan.pages+1 then observePage(scan) else scan.invalid=true end
   if onPage then return onPage(page,pages,lastPage) end
  end
  params.on_complete=function()
   if not pcall(finish,scan,'aux-addon','Aux') then B.say('Aux website export failed.') end
   if onComplete then return onComplete() end
  end
  return original(params)
 end
 installedAux=true
end

local function installAuctionator()
 if installedAuctionator or not IsAddOnLoaded('OctoAuctionator') or
    type(Atr_FullScanStart)~='function' or type(Atr_FullScanAnalyze)~='function' then return end
 local start,analyze=Atr_FullScanStart,Atr_FullScanAnalyze
 local scan
 Atr_FullScanStart=function()
  if gAtr_FullScanState~=ATR_FS_STARTED then
   scan=nil
   if B.realmOK() then scan=newScan() end
  end
  return start()
 end
 Atr_FullScanAnalyze=function()
  if not scan or gAtr_FullScanState~=ATR_FS_STARTED then return analyze() end
  -- Analyze reads the batch only when its private waiting flag is set.
  local current=scan
  local readCount=GetNumAuctionItems
  GetNumAuctionItems=function(kind)
   GetNumAuctionItems=readCount
   if kind=='list' then observePage(current) end
   return readCount(kind)
  end
  local ok,message=pcall(analyze)
  GetNumAuctionItems=readCount
  if not ok then scan=nil;error(message) end
  if gAtr_FullScanState~=ATR_FS_STARTED then
   scan=nil
   if not pcall(finish,current,'OctoAuctionator','OctoAuctionator') then B.say('OctoAuctionator website export failed.') end
  end
 end
 installedAuctionator=true
end

local function install()
 installAux()
 installAuctionator()
end
local frame=CreateFrame('Frame')
frame:RegisterEvent('ADDON_LOADED')
frame:RegisterEvent('PLAYER_LOGIN')
frame:SetScript('OnEvent',install)
install()
