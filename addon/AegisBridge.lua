-- Observe Aegis's completed unfiltered Full Scan. Never starts queries or trades.
local frame=CreateFrame('Frame')
local function install()
 local A=AegisExchange
 local B=TwilightHammerSync_Bridge
 if not A or not A.scan or not A.scan.Start or not B or A.scan.twilightBridge then return end
 local original=A.scan.Start
 A.scan.twilightBridge=true
 A.scan.Start=function(query,callbacks)
  local full=type(query)=='table' and not query[1]
  if full then for _,field in ipairs({'name','class','subclass','quality','minLevel','maxLevel','invType'}) do if query[field]~=nil and query[field]~='' then full=false end end end
  if not full or not B.realmOK() or (callbacks and callbacks.stampLast==false) then return original(query,callbacks) end
  local rows={}
  local listings={}
  local invalid=false
  local cb={};for k,v in pairs(callbacks or {}) do cb[k]=v end
  local oldListing=cb.onListing
  local oldComplete=cb.onComplete
  cb.onListing=function(itemId,name,count,buyout,minBid,owner)
   local ok=pcall(function()
    if not itemId or not name or not count or count<1 then invalid=true;return end
    listings[itemId]=listings[itemId] or {}
    table.insert(listings[itemId],{name=name,count=count,buyout=buyout or 0,startingBid=minBid or 0,seller=owner or ''})
    local row=rows[itemId]
    if not row then
     local meta=A.util and A.util.ItemInfo and A.util.ItemInfo(itemId) or {}
     local quality=meta.quality or (A.scan.state.tally and A.scan.state.tally.seen[itemId]) or 1
     local icon=string.lower(meta.texture or '')
     icon=string.gsub(icon,'\\','/');icon=string.gsub(icon,'^.*/','');icon=string.gsub(icon,'%.blp$','');icon=string.gsub(icon,'%.tga$','')
     if not string.find(icon,'^[a-z0-9_%-]+$') then icon='' end
     row={itemId=itemId,name=name,quality=quality,icon=icon,itemLevel=meta.itemLevel or 0,itemClass=meta.type or '',quantity=0,auctionCount=0,totalBuyout=0,prices={},sellers={}}
     rows[itemId]=row
    end
    row.quantity=row.quantity+count;row.auctionCount=row.auctionCount+1
    if buyout and buyout>0 then
     row.totalBuyout=row.totalBuyout+buyout
     table.insert(row.prices,math.floor(buyout/count))
    end
    if owner and owner~='' then row.sellers[owner]=true end
   end)
   if not ok then invalid=true end
   if oldListing then return oldListing(itemId,name,count,buyout,minBid,owner) end
  end
  cb.onComplete=function(stats)
   local ok=pcall(function()
    if invalid then B.say('Aegis scan had incomplete item data; previous website scan preserved.');return end
    local items={}
    for _,row in pairs(rows) do
     table.sort(row.prices)
     local n=table.getn(row.prices)
     row.minBuyout=row.prices[1];row.maxBuyout=row.prices[n]
     if n==0 then row.medianBuyout=nil elseif math.mod(n,2)==0 then row.medianBuyout=math.floor((row.prices[n/2]+row.prices[n/2+1])/2) else row.medianBuyout=row.prices[(n+1)/2] end
     local sellers=0;for _ in pairs(row.sellers) do sellers=sellers+1 end
     row.sellerCount=sellers;row.prices=nil;row.sellers=nil
     row.listings=listings[row.itemId] or {}
     table.insert(items,row)
    end
    if table.getn(items)>6000 then B.say('Aegis scan exceeds the website item limit; previous scan preserved.');return end
    local snapshot=B.base('auctions',items)
    snapshot.source='Aegis Exchange Full Scan '..(GetAddOnMetadata('Aegis_Exchange','Version') or '')
    local text=B.json(snapshot)
    if string.len(text)>890000 then B.say('Aegis scan exceeds the website upload limit; previous scan preserved.');return end
    B.save('auctions',snapshot)
    local exported=false
    if type(ExportFile)=='function' then exported=pcall(ExportFile,'TwilightHammerSync-Aegis-Auctions',text) end
    if exported then B.say('Aegis Full Scan exported for automatic website upload.') else B.say('Aegis Full Scan captured. /reload or log out to save it for upload.') end
   end)
   if not ok then B.say('Aegis website export failed; your Aegis scan is unaffected.') end
   if oldComplete then return oldComplete(stats) end
  end
  return original(query,cb)
 end
end
frame:RegisterEvent('ADDON_LOADED')
frame:SetScript('OnEvent',install)
install()
