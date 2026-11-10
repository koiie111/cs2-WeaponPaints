using WeaponPaints;
void Check(uint actual, uint expected, string message) { if(actual != expected) throw new Exception(message); }
Check(StickerAnchor.Resolve(0,0,3,0,0),3,"old default follows slot");
Check(StickerAnchor.Resolve(0,4,3,.01f,0),0,"old moved sticker uses anchor zero");
Check(StickerAnchor.Resolve(1,4,3,0,0),4,"explicit physical anchor differs from draw order");
Check(StickerAnchor.Resolve(1,6,0,.1f,-.1f),6,"physical anchors may exceed five draw-order slots");
Check(StickerAnchor.Resolve(1,0,4,0,0),0,"overlapping explicit anchor zero remains zero");
Console.WriteLine("PASS: 5 sticker format compatibility cases");
