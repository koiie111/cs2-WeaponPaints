using WeaponPaints;
void Check(uint actual, uint expected, string message) { if(actual != expected) throw new Exception(message); }
Check(StickerAnchor.Resolve(0,0,3,0,0),3,"old default follows slot");
Check(StickerAnchor.Resolve(0,4,3,.01f,0),0,"old moved sticker uses anchor zero");
Check(StickerAnchor.Resolve(1,4,3,0,0),4,"explicit physical anchor differs from draw order");
Check(StickerAnchor.Resolve(1,6,0,.1f,-.1f),6,"physical anchors may exceed five draw-order slots");
Check(StickerAnchor.Resolve(1,0,4,0,0),0,"overlapping explicit anchor zero remains zero");
Console.WriteLine("PASS: 5 sticker format compatibility cases");
void Choice(bool condition,string name){if(!condition)throw new Exception(name);}
Choice(!new WeaponInfo().HasCustomization,"legacy empty choice keeps random-skin fallback");
Choice(new WeaponInfo{CompositionVersion=1}.HasCustomization,"explicit default paint is a selection");
Choice(new WeaponInfo{Stickers=[new StickerInfo{Id=1}]}.HasCustomization,"stickers apply to default paint");
Choice(new WeaponInfo{KeyChain=new KeyChainInfo{Id=37,Sticker=1}}.HasCustomization,"slab applies to default paint");
Choice(new WeaponInfo{Nametag="test"}.HasCustomization,"nametag applies to default paint");
Choice(new WeaponInfo{StatTrak=true}.HasCustomization,"counter selection applies to default paint");
Console.WriteLine("PASS: 6 default-paint and decoration selection cases");
