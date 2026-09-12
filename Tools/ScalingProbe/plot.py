import sys,pathlib,json,math
sys.path.insert(0,r'D:/Projects/rimworld/work/sar-scaling-20260913/plot-deps')
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
out=pathlib.Path('D:/Projects/rimworld/Mods/SearchAndRescue/Docs/validation/2026-09-13-scaling')
r=json.loads((out/'measurements.json').read_text());by={x['name']:x for x in r}
plt.rcParams['font.family']='Microsoft YaHei';plt.rcParams['axes.unicode_minus']=False
fig,ax=plt.subplots(1,2,figsize=(12,4.6),layout='constrained')
groups=[('同时增加医生、搬运者、伤员',['d5p34h4r10','d10p68h8r7','d20p136h16r8','d40p272h32r9'],'#d45a43'),('只增加伤员',['d5p34h4r10','d5p68h4r4','d5p136h4r5','d5p272h4r6'],'#337f9a'),('只增加医生',['d5p34h4r10','d10p34h4r1','d20p34h4r2','d40p34h4r3'],'#659261')]
for label,names,col in groups:ax[0].plot([1,2,4,8],[by[n]['sarUsPerTick']/1000 for n in names],marker='o',label=label,color=col,lw=2)
ax[0].set(xscale='log',yscale='log',xlabel='人数相对于基线的倍数',ylabel='SAR 组件耗时（ms / 游戏 tick）',title='实际救援：同时扩大会快速恶化')
ax[0].set_xticks([1,2,4,8],['1×','2×','4×','8×']);ax[0].legend(fontsize=8,loc='upper left');ax[0].grid(True,which='both',alpha=.15)
names=groups[1][1];p=[34,68,136,272];vals=[by[n]['solver']['medianMs'] for n in names]
ax[1].plot(p,vals,marker='o',color='#7753a0',lw=2,label='游戏内匹配器：7 次计时中位数')
ax[1].plot(p,[vals[0]*(v/34)**3 for v in p],ls='--',color='#999999',label='伤员数的三次方参考线')
ax[1].set(xscale='log',yscale='log',xlabel='伤员数（固定 5 医生 + 4 搬运者）',ylabel='一次匹配求解耗时（ms）',title='方阵匹配：接近立方增长');ax[1].set_xticks(p,list(map(str,p)));ax[1].legend(fontsize=8);ax[1].grid(True,which='both',alpha=.15)
for a in ax:a.xaxis.set_minor_locator(matplotlib.ticker.NullLocator())
fig.suptitle('Search and Rescue 大规模救援压测',fontsize=15)
fig.savefig(out/'scaling.png',dpi=160);fig.savefig(out/'scaling.svg')

