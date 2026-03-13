# PhotoSelector 评分与规则说明

## 1. 当前评分机制
- 综合分 `overall_score` 取值范围 `0~1`
- 计算公式:
  - `overall = sharpness*0.35 + exposure*0.25 + object*0.20 + face*0.12 + style*0.08`
- 分项来源:
  - `sharpness`: 拉普拉斯方差归一化
  - `exposure`: 灰度均值偏离 128 的程度
  - `object`: YOLO 检测结果平均置信度
  - `face`: 人脸数量和 curation 插件分数组合
  - `style`: 基于 HSV 的风格分

## 2. 废片判定规则
- 满足任一条件会标记为废片 `is_waste=true`:
  - `sharpness_score < 0.12` -> `blur`
  - `exposure_score < 0.18` -> `bad_exposure`
  - `face_count == 0 && overall_score < 0.3` -> `no_subject`
  - `overall_score < 0.22` -> `low_score`

## 3. 风格判定规则
- `low_saturation`: 平均饱和度 < 35
- `warm`: 色相均值在 10~40
- `cool`: 色相均值在 90~140
- 其他为 `neutral`

## 4. 分组维度
- 人像维度:
  - `person:any` 有人像
  - `person:multi` 多人像
  - `person:none` 无人像
- 废片维度:
  - `waste:true`
  - `waste:false`
- 风格维度:
  - `style:warm / style:cool / style:neutral / style:low_saturation`
- 质量维度:
  - `top:10` Top 10%
  - `unanalyzed` 未分析

## 5. DSL 规则写法
- 支持示例:
  - `overall>0.65 AND sharpness>0.4 AND is_waste==false`
  - `facecount>0 AND style==warm AND duplicate==false`
  - `overall>0.7 AND eyes_closed==false`

## 6. 可调参数位置
- 文件: `python-ai-service/appsettings.json`
- 可调项:
  - `engine.cpu_workers`
  - `engine.batch_parallelism`
  - `engine.score_weights`

## 7. 调整建议
- 人像婚礼场景:
  - 提高 `face` 权重到 `0.18`
  - 降低 `object` 权重到 `0.14`
- 棚拍风格场景:
  - 提高 `style` 权重到 `0.15`
  - 降低 `object` 权重到 `0.12`
