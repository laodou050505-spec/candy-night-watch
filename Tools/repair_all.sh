#!/bin/zsh
# 批量拓扑修复：每个模型 model.glb → repaired.glb（4 并发）
cd "${0:a:h}/.."
B=/Applications/Blender.app/Contents/MacOS/Blender
typeset -A CAP
CAP=(E1_Skelly 16000 E2_Batsy 14000 E3_Dangle 12000 E4_Jack 18000 E5_Mitten 14000 E6_Boo 10000 E7_Frankenbear 24000
S1_PumpkinTable 40000 S2_CandyBucket 20000 S3_Cauldron 20000 S4_BookTower 30000 S5_WitchBed 36000 S6_FangWardrobe 26000 S7_Coffin 16000
S8_PumpkinPile 24000 S9_Broom 12000 S10_CandyWrapped 6000 S11_LollipopPumpkin 6000 S12_Gravestone 10000
L1_ArchColumn 30000 L2_RoseWindow 30000 L3_HauntedManor 45000 L4_WitchCottage 40000 L5_Scarecrow 20000 L6_BellTower 30000
L7_FloatIsland 40000 L8_RopeBridgePost 12000 L9_MushroomLamp 14000 L10_CandleCluster 10000
H1_JackLanternBig 18000 H2_SpookyTree 30000 H3_GraveyardSet 26000 H4_WitchOnBroom 20000 H5_SkullCandelabra 14000 H6_BatLantern 10000 H7_CandyCornPile 12000 H8_PotionShelf 22000)
names=(${@:-${(k)CAP}})
run() { local n=$1; local g=ArtSource/Models/$n/model.glb; [[ -f $g ]] || { echo "$n 跳过"; return; }
  $B -b -P Tools/blender_repair.py -- $g ArtSource/Models/$n/repaired.glb ${CAP[$n]} > ArtSource/Models/$n/repair.log 2>&1
  echo "$n exit=$? $(grep REPAIR ArtSource/Models/$n/repair.log | cut -c1-160)"; }
for n in $names; do run $n & while (( $(jobs -r | wc -l) >= 4 )); do sleep 2; done; done; wait
