-- Vanilla 1.12 / Lua 5.0. No purchases, sales, mail, movement or login automation.
local frame=CreateFrame('Frame')
local edition=TwilightHammerSync_Edition
local auctionOnly=edition=='auctions'
local bankEnabled=not edition or edition=='bank'
local bankOpen=false
local ahOpen=false
local scan=nil
local liveSaved=false
local automatic=false
local pending={}
local professionOpen=false
local nextRoster=0
local function queue(kind,delay)
 pending[kind]=GetTime()+(delay or 2)
end
local bankNames={twilightbank=true,twilightbags=true}
local function say(s) if not automatic then DEFAULT_CHAT_FRAME:AddMessage('|cffbdabe8Twilight Hammer:|r '..s) end end
local function quote(s)
 return '"'..string.gsub(tostring(s),'[%z\1-\31\\"]',function(c)
  if c=='"' then return '\\"' elseif c=='\\' then return '\\\\' end
  return string.format('\\u%04x',string.byte(c))
 end)..'"'
end
local function json(v,array)
 local t=type(v)
 if t=='string' then return quote(v) elseif t=='number' then return tostring(v) elseif t=='boolean' then if v then return 'true' else return 'false' end elseif t~='table' then return 'null' end
 local out={}
 if array or v[1] then for i=1,table.getn(v) do table.insert(out,json(v[i])) end;return '['..table.concat(out,',')..']' end
 for k,x in pairs(v) do table.insert(out,quote(k)..':'..json(x,k=='items' or k=='reagents')) end
 return '{'..table.concat(out,',')..'}'
end
local function realmOK()
 local r=string.lower(GetRealmName() or '')
 r=string.gsub(r,'[^%a%d]','')
 return r=='yshaarj' and UnitFactionGroup('player')=='Alliance'
end
local function base(kind,items)
 return {schemaVersion=1,kind=kind,complete=true,realm="Y'Shaarj",faction='Alliance',character=UnitName('player'),observedAt=time()*1000,items=items,source='Twilight Hammer Sync 0.2.5',guild=GetGuildInfo('player') or ''}
end
local function save(key,value)
 if not realmOK() then say("Capture skipped: this package is configured for Y'Shaarj Alliance.");return end
 if auctionOnly and value.kind~='auctions' then return end
 if not bankEnabled and (value.kind=='wallet' or value.kind=='inventory') then return end
 TwilightHammerSync_Captures=TwilightHammerSync_Captures or {}
 TwilightHammerSync_Captures[key]=value
 local list={}
 for _,s in pairs(TwilightHammerSync_Captures) do
  if s.kind=='auctions' or (not auctionOnly and (bankEnabled or s.kind=='professions' or s.kind=='roster')) then
   table.insert(list,s)
  end
 end
 TwilightHammerSync_Export=json(list,true)
 liveSaved=false
 if type(ExportFile)=='function' then
  local ok,result=pcall(ExportFile,'TwilightHammerSync-Captures',TwilightHammerSync_Export)
  liveSaved=ok and result~=false
 end
 return liveSaved
end
local function savedMessage(message)
 if liveSaved then say(message..' Saved for automatic upload; no reload needed.')
 else say(message..' Live file export unavailable. /reload or log out to save.') end
end
local function id(link) if not link then return nil end;local _,_,n=string.find(link,'item:(%d+)');return tonumber(n) end
local function captureWallet()
 local name=UnitName('player')
 if not name or not bankNames[string.lower(name)] then return end
 local copper=GetMoney()
 if type(copper)~='number' or copper<0 then return end
 save(string.lower(name)..':wallet',base('wallet',{{name='Character funds',copper=copper}}))
end
local function captureInventory(location)
 local name=UnitName('player')
 if not bankNames[string.lower(name)] then return end
 if location=='bank' and not bankOpen then say('Open the bank before capturing it.');return end
 local bags={0,1,2,3,4}
 if location=='bank' then bags={-1};for b=5,4+(NUM_BANKBAGSLOTS or 6) do table.insert(bags,b) end end
 local totals={}
 for _,b in ipairs(bags) do
  for slot=1,GetContainerNumSlots(b) do
   local texture,count=GetContainerItemInfo(b,slot)
   if texture then
    local itemId=id(GetContainerItemLink(b,slot))
    local itemName,_,quality
    if itemId then itemName,_,quality=GetItemInfo('item:'..itemId..':0:0:0') end
    if not itemId or not itemName or not count then if automatic then queue(location,3) end;say('Items are still loading. Capture again in a moment; previous stock is preserved.');return end
    local item=totals[itemId] or {itemId=itemId,name=itemName,quality=quality or 1,count=0}
    item.count=item.count+count;totals[itemId]=item
   end
  end
 end
 local items={};for _,item in pairs(totals) do table.insert(items,item) end
 local s=base('inventory',items);s.location=location;save(string.lower(name)..':'..location,s);captureWallet()
 savedMessage(location..' captured for '..name..'.')
end
local function captureRoster()
 if not IsInGuild() then say('Join the guild before capturing a roster.');return end
 if edition and string.lower(GetGuildInfo('player') or '')~='twilight hammer' then return end
 local total=GetNumGuildMembers(true)
 if not total or total==0 then say('Roster has not loaded. Open the guild window and try again.');return end
 local items={}
 for i=1,total do
  local name,rank,_,level,class,_,_,_,online=GetGuildRosterInfo(i)
  if not name or not level or level<1 then say('Incomplete guild roster; previous snapshot preserved.');return end
  table.insert(items,{name=name,rank=rank or '',class=class or '',level=level,online=online and true or false})
 end
 save('roster:'..(GetGuildInfo('player') or ''),base('roster',items));savedMessage('Guild roster captured.')
end
local function captureProfessions()
 local profession,skill=GetTradeSkillLine()
 if not profession or profession=='UNKNOWN' or GetNumTradeSkills()==0 then say('Open a profession window first. Enchanting craft-window capture is not available in this version.');return end
 -- Expanding headers exposes learned recipes without performing a craft.
 local i=1
 while i<=GetNumTradeSkills() do local _,kind,_,expanded=GetTradeSkillInfo(i);if kind=='header' and not expanded then ExpandTradeSkillSubClass(i) end;i=i+1 end
 local items={}
 local prior=TwilightHammerSync_Captures and TwilightHammerSync_Captures[string.lower(UnitName('player'))..':professions']
 if prior then for _,r in ipairs(prior.items) do if r.profession~=profession then table.insert(items,r) end end end
 for index=1,GetNumTradeSkills() do
  local name,kind=GetTradeSkillInfo(index)
  if kind~='header' then
   local reagents={}
   for r=1,GetTradeSkillNumReagents(index) do
    local reagentName,_,count=GetTradeSkillReagentInfo(index,r)
    local itemId=id(GetTradeSkillReagentItemLink(index,r))
    if not reagentName or not itemId or not count then if automatic then queue('professions',3) end;say('Recipe data is loading. Previous recipes preserved; try again.');return end
    table.insert(reagents,{itemId=itemId,name=reagentName,count=count})
   end
   local minMade=GetTradeSkillNumMade(index)
   table.insert(items,{name=name,profession=profession,skill=skill or 0,itemId=id(GetTradeSkillItemLink(index)) or 0,yield=minMade or 1,reagents=reagents})
  end
 end
 save(string.lower(UnitName('player'))..':professions',base('professions',items));savedMessage('Learned recipes captured.')
end
local function cancelScan(reason) scan=nil;say(reason..' Previous complete auction capture is preserved.') end
local function queryPage()
 if not scan or not ahOpen then return end
 if GetTime()-scan.started>1800 then cancelScan('Scan timed out.');return end
 if not CanSendAuctionQuery() then return end
 scan.waiting=true;scan.sent=GetTime()
 QueryAuctionItems('',nil,nil,nil,nil,nil,scan.page,false,nil)
end
local function receivePage()
 if not scan or not scan.waiting then return end
 local count,total=GetNumAuctionItems('list')
 if not total or not count then return end
 if scan.total and total~=scan.total then cancelScan('Auction count changed during the scan. Please restart.');return end
 scan.total=total
 local expected=math.min(50,math.max(0,total-scan.page*50))
 if count~=expected then cancelScan('An incomplete auction page was returned.');return end
 local page={}
 for i=1,count do
  local name,texture,quantity,quality,usable,level,minBid,increment,buyout,bid,highBidder,seller=GetAuctionItemInfo('list',i)
  local itemId=id(GetAuctionItemLink('list',i))
  if not name or not itemId or not quantity or quantity<1 or buyout==nil then return end
  if buyout>0 then
   local row=page[itemId] or {itemId=itemId,name=name,quality=quality or 1,itemLevel=level or 0,itemClass='',quantity=0,auctionCount=0,totalBuyout=0,prices={},sellers={}}
   row.quantity=row.quantity+quantity;row.auctionCount=row.auctionCount+1;row.totalBuyout=row.totalBuyout+buyout
   table.insert(row.prices,math.floor(buyout/quantity))
   if seller then row.sellers[seller]=true end
   page[itemId]=row
  end
 end
 for itemId,r in pairs(page) do
  local old=scan.rows[itemId]
  if old then old.quantity=old.quantity+r.quantity;old.auctionCount=old.auctionCount+r.auctionCount;old.totalBuyout=old.totalBuyout+r.totalBuyout;for _,v in ipairs(r.prices) do table.insert(old.prices,v) end;for seller in pairs(r.sellers) do old.sellers[seller]=true end else scan.rows[itemId]=r end
 end
 scan.waiting=false
 if (scan.page+1)*50>=total then
  local items={}
  for _,r in pairs(scan.rows) do
   table.sort(r.prices)
   local n=table.getn(r.prices);r.minBuyout=r.prices[1];r.maxBuyout=r.prices[n];r.medianBuyout=r.prices[math.ceil(n/2)]
   local sellers=0;for _ in pairs(r.sellers) do sellers=sellers+1 end;r.sellerCount=sellers;r.prices=nil;r.sellers=nil
   table.insert(items,r)
  end
  if table.getn(items)>6000 then cancelScan('More than 6,000 distinct items; scan cannot fit this importer.');return end
  save('auctions',base('auctions',items));scan=nil;savedMessage('Complete buyout auction scan captured.')
 else scan.page=scan.page+1;say('Auction page '..scan.page..' captured. Continue with /thsync ahnext. Do not use another auction search while scanning.') end
end
TwilightHammerSync_Bridge={save=save,base=base,json=json,say=say,realmOK=realmOK}
SLASH_TWILIGHTHAMMERSYNC1='/thsync'
SlashCmdList['TWILIGHTHAMMERSYNC']=function(msg)
 msg=string.lower(msg or '')
 if msg=='bags' and bankEnabled then captureInventory('bags')
 elseif msg=='bank' and bankEnabled then captureInventory('bank')
 elseif msg=='roster' and not auctionOnly then captureRoster()
 elseif msg=='professions' and not auctionOnly then captureProfessions()
 elseif msg=='ah' then
  if not ahOpen then say('Open the Auction House first.');return end
  scan={page=0,rows={},started=GetTime(),waiting=false};say('Auction scan started. Each page needs /thsync ahnext after the previous page loads.');queryPage()
 elseif msg=='ahnext' then if scan and not scan.waiting then queryPage() else say('No next page ready. Wait for the current page, or start /thsync ah.') end
 elseif msg=='cancel' then cancelScan('Scan cancelled.')
 else
  local commands=bankEnabled and 'bags | bank | ' or ''
  if not auctionOnly then commands=commands..'roster | professions | ' end
  say('/thsync '..commands..'ah | ahnext | cancel')
 end
end
for _,e in ipairs({'VARIABLES_LOADED','AUCTION_HOUSE_SHOW','AUCTION_HOUSE_CLOSED','AUCTION_ITEM_LIST_UPDATE'}) do frame:RegisterEvent(e) end
if not auctionOnly then
 for _,e in ipairs({'PLAYER_ENTERING_WORLD','GUILD_ROSTER_UPDATE','PLAYER_GUILD_UPDATE','TRADE_SKILL_SHOW','TRADE_SKILL_UPDATE','TRADE_SKILL_CLOSE'}) do frame:RegisterEvent(e) end
end
if bankEnabled then
 for _,e in ipairs({'PLAYER_MONEY','BAG_UPDATE','PLAYERBANKSLOTS_CHANGED','PLAYERBANKBAGSLOTS_CHANGED','BANKFRAME_OPENED','BANKFRAME_CLOSED'}) do frame:RegisterEvent(e) end
end
frame:SetScript('OnEvent',function()
 if event=='VARIABLES_LOADED' then TwilightHammerSync_Captures=TwilightHammerSync_Captures or {};say('Loaded. Automatic captures enabled; /thsync commands remain available.')
 elseif event=='PLAYER_ENTERING_WORLD' then
  if bankEnabled then queue('wallet');queue('bags',4) end
  nextRoster=GetTime()+3
 elseif event=='PLAYER_MONEY' then queue('wallet')
 elseif event=='BAG_UPDATE' then queue('bags');if bankOpen then queue('bank') end
 elseif event=='PLAYERBANKSLOTS_CHANGED' or event=='PLAYERBANKBAGSLOTS_CHANGED' then if bankOpen then queue('bank') end
 elseif event=='GUILD_ROSTER_UPDATE' then queue('roster',3)
 elseif event=='PLAYER_GUILD_UPDATE' then nextRoster=GetTime()+3
 elseif event=='TRADE_SKILL_SHOW' then professionOpen=true;queue('professions')
 elseif event=='TRADE_SKILL_UPDATE' then if professionOpen then queue('professions') end
 elseif event=='TRADE_SKILL_CLOSE' then professionOpen=false;pending.professions=nil
 elseif event=='BANKFRAME_OPENED' then bankOpen=true;queue('bank');queue('bags')
 elseif event=='BANKFRAME_CLOSED' then bankOpen=false;pending.bank=nil
 elseif event=='AUCTION_HOUSE_SHOW' then ahOpen=true
 elseif event=='AUCTION_HOUSE_CLOSED' then ahOpen=false;if scan then cancelScan('Auction window closed.') end
 elseif event=='AUCTION_ITEM_LIST_UPDATE' then receivePage() end
end)
frame:SetScript('OnUpdate',function()
 local now=GetTime()
 if scan and scan.waiting and now-scan.sent>45 then cancelScan('Auction page did not finish loading.') end
 if nextRoster>0 and now>=nextRoster then nextRoster=now+300;if realmOK() and IsInGuild() and GuildRoster then GuildRoster() end end
 for kind,due in pairs(pending) do
  if now>=due then
   pending[kind]=nil
   automatic=true
   if realmOK() then
    if kind=='wallet' then captureWallet()
    elseif kind=='bags' then captureInventory('bags')
    elseif kind=='bank' and bankOpen then captureInventory('bank')
    elseif kind=='roster' then captureRoster()
    elseif kind=='professions' and professionOpen then captureProfessions() end
   end
   automatic=false
  end
 end
end)
