#!/bin/zsh
# 最小拓扑修复（保留原网格与原贴图）：model.glb → repaired.glb（5 并发）
cd "${0:a:h}/.."
B=/Applications/Blender.app/Contents/MacOS/Blender
typeset -A RULE
RULE=(S2_CandyBucket bucket L10_CandleCluster candles H5_SkullCandelabra candles)
names=(${@:-$(ls ArtSource/Models)})
run() { local n=$1; local g=ArtSource/Models/$n/model.glb; [[ -f $g ]] || { echo "$n 跳过"; return; }
  [[ -f ArtSource/Models/$n/repaired.glb && ! -f ArtSource/Models/$n/repaired_voxel.glb ]] && mv ArtSource/Models/$n/repaired.glb ArtSource/Models/$n/repaired_voxel.glb
  $B -b -P Tools/blender_repair_min.py -- $g ArtSource/Models/$n/repaired.glb ${RULE[$n]:--} > ArtSource/Models/$n/repair_min.log 2>&1
  echo "$n exit=$? $(grep REPAIRMIN ArtSource/Models/$n/repair_min.log | cut -c1-400)"; }
for n in $names; do run $n & while (( $(jobs -r | wc -l) >= 5 )); do sleep 1; done; done; wait
