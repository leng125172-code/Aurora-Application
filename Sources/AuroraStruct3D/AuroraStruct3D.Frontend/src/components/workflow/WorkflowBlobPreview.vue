<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import { OBJLoader } from 'three/examples/jsm/loaders/OBJLoader.js'
import { PCDLoader } from 'three/examples/jsm/loaders/PCDLoader.js'
import { PLYLoader } from 'three/examples/jsm/loaders/PLYLoader.js'
import { STLLoader } from 'three/examples/jsm/loaders/STLLoader.js'

import { httpClient } from '@/api/client'
import WorkflowJsonTree from '@/components/workflow/WorkflowJsonTree.vue'
import {
    workflowResultFileExtension,
    workflowResultFileName,
    type WorkflowResultPresentation,
} from '@/utils/workflow-result'

const props = defineProps<{
    blobKey: string
    presentation: WorkflowResultPresentation
}>()

const loading = ref(false)
const error = ref('')
const objectUrl = ref('')
const textContent = ref('')
const jsonContent = ref<unknown>()
const canvasRef = ref<HTMLCanvasElement>()

let renderer: THREE.WebGLRenderer | undefined
let controls: OrbitControls | undefined
let animationFrame = 0
let sceneObject: THREE.Object3D | undefined

async function fetchBlob(): Promise<Blob> {
    const endpoint = props.presentation === 'image'
        ? '/api/app/operator-file/preview'
        : '/api/app/operator-file/download'
    const response = await httpClient.get(endpoint, {
        params: { blobName: props.blobKey },
        responseType: 'blob',
    })
    return response.data as Blob
}

function clearObjectUrl() {
    if (objectUrl.value) URL.revokeObjectURL(objectUrl.value)
    objectUrl.value = ''
}

function disposeObject(object: THREE.Object3D) {
    object.traverse((child) => {
        if (child instanceof THREE.Mesh || child instanceof THREE.Points) {
            child.geometry?.dispose()
            const materials = Array.isArray(child.material) ? child.material : [child.material]
            materials.forEach((material) => material?.dispose())
        }
    })
}

function disposeThree() {
    if (animationFrame) cancelAnimationFrame(animationFrame)
    animationFrame = 0
    controls?.dispose()
    controls = undefined
    if (sceneObject) disposeObject(sceneObject)
    sceneObject = undefined
    renderer?.dispose()
    renderer = undefined
}

function pointGeometryFromText(text: string): THREE.BufferGeometry {
    const values: number[] = []
    for (const rawLine of text.split(/\r?\n/)) {
        const line = rawLine.trim()
        if (!line || line.startsWith('#')) continue
        const columns = line.split(/[\s,;]+/).map(Number)
        if (columns.length >= 3 && columns.slice(0, 3).every(Number.isFinite)) {
            values.push(columns[0]!, columns[1]!, columns[2]!)
        }
    }
    if (!values.length) throw new Error('文件中没有可识别的 XYZ 点坐标。')
    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(values, 3))
    return geometry
}

function pointsFromGeometry(geometry: THREE.BufferGeometry): THREE.Points {
    return new THREE.Points(
        geometry,
        new THREE.PointsMaterial({
            size: 0.02,
            color: 0x22d3ee,
            vertexColors: geometry.hasAttribute('color'),
            sizeAttenuation: true,
        }),
    )
}

async function parseThreeObject(buffer: ArrayBuffer): Promise<THREE.Object3D> {
    const extension = workflowResultFileExtension(props.blobKey)
    if (extension === 'pcd') return new PCDLoader().parse(buffer)
    if (extension === 'obj') return new OBJLoader().parse(new TextDecoder().decode(buffer))
    if (extension === 'stl') {
        const geometry = new STLLoader().parse(buffer)
        return new THREE.Mesh(
            geometry,
            new THREE.MeshStandardMaterial({ color: 0x94a3b8 }),
        )
    }
    if (extension === 'glb' || extension === 'gltf') {
        const gltf = await new GLTFLoader().parseAsync(buffer, '')
        return gltf.scene
    }
    if (extension === 'xyz' || extension === 'pts' || extension === 'asc') {
        return pointsFromGeometry(pointGeometryFromText(new TextDecoder().decode(buffer)))
    }

    const geometry = new PLYLoader().parse(buffer)
    if (geometry.index) {
        geometry.computeVertexNormals()
        return new THREE.Mesh(
            geometry,
            new THREE.MeshStandardMaterial({
                color: 0x94a3b8,
                vertexColors: geometry.hasAttribute('color'),
            }),
        )
    }
    return pointsFromGeometry(geometry)
}

async function initThree(buffer: ArrayBuffer) {
    await nextTick()
    const canvas = canvasRef.value
    if (!canvas) return

    renderer = new THREE.WebGLRenderer({ canvas, antialias: true })
    renderer.setPixelRatio(window.devicePixelRatio)
    renderer.setSize(canvas.clientWidth || 720, canvas.clientHeight || 360, false)

    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0x111827)
    scene.add(new THREE.AmbientLight(0xffffff, 0.8))
    const light = new THREE.DirectionalLight(0xffffff, 1)
    light.position.set(5, 10, 7)
    scene.add(light)
    scene.add(new THREE.GridHelper(10, 10, 0x475569, 0x334155))

    const camera = new THREE.PerspectiveCamera(
        60,
        (canvas.clientWidth || 720) / (canvas.clientHeight || 360),
        0.01,
        100000,
    )
    controls = new OrbitControls(camera, canvas)
    controls.enableDamping = true

    sceneObject = await parseThreeObject(buffer)
    scene.add(sceneObject)
    const box = new THREE.Box3().setFromObject(sceneObject)
    const center = box.getCenter(new THREE.Vector3())
    const size = box.getSize(new THREE.Vector3())
    sceneObject.position.sub(center)
    const maxDimension = Math.max(size.x, size.y, size.z, 1)
    camera.position.set(maxDimension, maxDimension, maxDimension * 1.5)
    controls.target.set(0, 0, 0)
    controls.update()

    const animate = () => {
        animationFrame = requestAnimationFrame(animate)
        controls?.update()
        renderer?.render(scene, camera)
    }
    animate()
}

async function loadPreview() {
    clearObjectUrl()
    disposeThree()
    textContent.value = ''
    jsonContent.value = undefined
    error.value = ''
    loading.value = true
    try {
        if (props.presentation === 'cad' || props.presentation === 'file') {
            return
        }
        const blob = await fetchBlob()
        if (props.presentation === 'image') {
            objectUrl.value = URL.createObjectURL(blob)
        } else if (
            props.presentation === 'point-cloud'
            || props.presentation === 'model-3d'
        ) {
            // 加载态会隐藏 canvas，先切换模板再初始化 WebGL。
            loading.value = false
            await nextTick()
            await initThree(await blob.arrayBuffer())
        } else if (
            props.presentation === 'json-file'
            || props.presentation === 'text-file'
        ) {
            const text = await blob.text()
            if (props.presentation === 'json-file') {
                jsonContent.value = JSON.parse(text)
            } else {
                textContent.value = text
            }
        }
    } catch (cause) {
        error.value = cause instanceof Error ? cause.message : String(cause)
    } finally {
        loading.value = false
    }
}

async function downloadBlob() {
    try {
        const blob = await fetchBlob()
        const url = URL.createObjectURL(blob)
        const anchor = document.createElement('a')
        anchor.href = url
        anchor.download = workflowResultFileName(props.blobKey) || 'workflow-result'
        anchor.click()
        URL.revokeObjectURL(url)
    } catch (cause) {
        error.value = cause instanceof Error ? cause.message : String(cause)
    }
}

watch(() => [props.blobKey, props.presentation], () => void loadPreview())
onMounted(() => void loadPreview())
onBeforeUnmount(() => {
    clearObjectUrl()
    disposeThree()
})
</script>

<template>
    <div class="rounded border border-border bg-background p-3">
        <div class="mb-2 flex items-center justify-between gap-3">
            <div class="min-w-0">
                <div class="truncate font-medium">{{ workflowResultFileName(blobKey) }}</div>
                <div class="truncate text-xs text-muted-foreground">{{ blobKey }}</div>
            </div>
            <button class="ide-button shrink-0" type="button" @click="downloadBlob">下载</button>
        </div>
        <div v-if="loading" class="py-6 text-center text-muted-foreground">正在加载预览…</div>
        <div v-else-if="error" class="rounded bg-red-500/10 px-3 py-2 text-red-600">{{ error }}</div>
        <img
            v-else-if="presentation === 'image' && objectUrl"
            :src="objectUrl"
            class="max-h-[28rem] max-w-full rounded object-contain"
            alt="工作流结果图片"
        />
        <canvas
            v-else-if="presentation === 'point-cloud' || presentation === 'model-3d'"
            ref="canvasRef"
            class="h-[24rem] w-full rounded"
        />
        <div
            v-else-if="presentation === 'json-file'"
            class="max-h-[28rem] overflow-auto rounded bg-muted/40 p-3"
        >
            <WorkflowJsonTree :value="jsonContent" />
        </div>
        <pre
            v-else-if="presentation === 'text-file'"
            class="max-h-[28rem] overflow-auto whitespace-pre-wrap rounded bg-muted/40 p-3 text-xs"
        >{{ textContent }}</pre>
        <div v-else class="py-4 text-sm text-muted-foreground">
            当前格式不支持浏览器内预览，请下载后打开。
        </div>
    </div>
</template>
