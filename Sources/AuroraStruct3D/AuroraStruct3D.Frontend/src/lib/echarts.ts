import { BarChart, LineChart, ScatterChart } from 'echarts/charts'
import {
    GridComponent,
    LegendComponent,
    MarkLineComponent,
    TooltipComponent,
    VisualMapComponent,
} from 'echarts/components'
import * as echarts from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'

// Central registry keeps the chart runtime tree-shakeable.
echarts.use([
    BarChart,
    LineChart,
    ScatterChart,
    GridComponent,
    LegendComponent,
    MarkLineComponent,
    TooltipComponent,
    VisualMapComponent,
    CanvasRenderer,
])

export { echarts }
