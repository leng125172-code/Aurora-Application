<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'

const props = defineProps<{
    pointData: Float32Array | null
    hasColor: boolean
}>()

const containerRef = ref<HTMLDivElement | null>(null)

let scene: THREE.Scene
let camera: THREE.PerspectiveCamera
let renderer: THREE.WebGLRenderer
let controls: OrbitControls
let pointCloud: THREE.Points
let animationFrameId: number | null = null

const pointCount = ref(0)
const bounds = ref({ x: 0, y: 0, z: 0 })

function initScene() {
    if (!containerRef.value) return

    const width = containerRef.value.clientWidth
    const height = containerRef.value.clientHeight

    scene = new THREE.Scene()
    scene.background = new THREE.Color(0x1a1a2e)

    camera = new THREE.PerspectiveCamera(60, width / height, 0.1, 1000)
    camera.position.set(0, 2, 5)

    renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setSize(width, height)
    renderer.setPixelRatio(window.devicePixelRatio)
    containerRef.value.appendChild(renderer.domElement)

    controls = new OrbitControls(camera, renderer.domElement)
    controls.enableDamping = true
    controls.dampingFactor = 0.05
    controls.minDistance = 0.5
    controls.maxDistance = 20

    const ambientLight = new THREE.AmbientLight(0xffffff, 0.6)
    scene.add(ambientLight)

    const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8)
    directionalLight.position.set(5, 10, 7)
    scene.add(directionalLight)

    const gridHelper = new THREE.GridHelper(10, 10, 0x444444, 0x333333)
    scene.add(gridHelper)

    animate()
}

function animate() {
    animationFrameId = requestAnimationFrame(animate)
    controls.update()
    renderer.render(scene, camera)
}

function updatePointCloud(data: Float32Array, hasColor: boolean) {
    if (!data || data.length === 0) return

    const stride = hasColor ? 6 : 3
    const count = data.length / stride

    pointCount.value = count

    if (pointCloud) {
        scene.remove(pointCloud)
        ;(pointCloud.geometry as THREE.BufferGeometry).dispose()
        ;(pointCloud.material as THREE.Material).dispose()
    }

    const geometry = new THREE.BufferGeometry()
    const positions = new Float32Array(count * 3)
    const colors = hasColor ? new Float32Array(count * 3) : null

    let minX = Infinity, maxX = -Infinity
    let minY = Infinity, maxY = -Infinity
    let minZ = Infinity, maxZ = -Infinity

    for (let i = 0; i < count; i++) {
        const srcIdx = i * stride
        const dstIdx = i * 3

        positions[dstIdx] = data[srcIdx]
        positions[dstIdx + 1] = data[srcIdx + 1]
        positions[dstIdx + 2] = data[srcIdx + 2]

        if (hasColor && colors) {
            colors[dstIdx] = data[srcIdx + 3]
            colors[dstIdx + 1] = data[srcIdx + 4]
            colors[dstIdx + 2] = data[srcIdx + 5]
        }

        minX = Math.min(minX, positions[dstIdx])
        maxX = Math.max(maxX, positions[dstIdx])
        minY = Math.min(minY, positions[dstIdx + 1])
        maxY = Math.max(maxY, positions[dstIdx + 1])
        minZ = Math.min(minZ, positions[dstIdx + 2])
        maxZ = Math.max(maxZ, positions[dstIdx + 2])
    }

    bounds.value = {
        x: maxX - minX,
        y: maxY - minY,
        z: maxZ - minZ,
    }

    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3))
    if (hasColor && colors) {
        geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3))
    }

    const material = new THREE.PointsMaterial({
        size: 0.02,
        vertexColors: hasColor,
        color: 0x00ffff,
        transparent: true,
        opacity: 0.8,
        sizeAttenuation: true,
    })

    pointCloud = new THREE.Points(geometry, material)
    scene.add(pointCloud)

    const centerX = (minX + maxX) / 2
    const centerY = (minY + maxY) / 2
    const centerZ = (minZ + maxZ) / 2
    const maxDim = Math.max(maxX - minX, maxY - minY, maxZ - minZ)

    controls.target.set(centerX, centerY, centerZ)
    camera.position.set(centerX, centerY + maxDim, centerZ + maxDim * 1.5)
    controls.update()
}

function handleResize() {
    if (!containerRef.value || !camera || !renderer) return

    const width = containerRef.value.clientWidth
    const height = containerRef.value.clientHeight

    camera.aspect = width / height
    camera.updateProjectionMatrix()
    renderer.setSize(width, height)
}

watch(
    () => props.pointData,
    (newData) => {
        if (newData) {
            updatePointCloud(newData, props.hasColor)
        }
    }
)

onMounted(() => {
    initScene()
    window.addEventListener('resize', handleResize)
})

onUnmounted(() => {
    window.removeEventListener('resize', handleResize)
    if (animationFrameId !== null) cancelAnimationFrame(animationFrameId)

    if (pointCloud) {
        ;(pointCloud.geometry as THREE.BufferGeometry).dispose()
        ;(pointCloud.material as THREE.Material).dispose()
    }

    if (renderer) {
        renderer.dispose()
        if (containerRef.value && renderer.domElement) {
            containerRef.value.removeChild(renderer.domElement)
        }
    }

    if (controls) {
        controls.dispose()
    }
})
</script>

<template>
    <div class="relative h-full w-full">
        <div ref="containerRef" class="h-full w-full" />
        <div class="absolute bottom-3 left-3 rounded-lg bg-black/60 px-3 py-2 text-xs text-white">
            <div class="flex items-center gap-4">
                <span>点数：<strong>{{ pointCount.toLocaleString() }}</strong></span>
                <span>尺寸：<strong>{{ bounds.x.toFixed(2) }} × {{ bounds.y.toFixed(2) }} × {{ bounds.z.toFixed(2) }}</strong></span>
            </div>
        </div>
        <div class="absolute top-3 right-3 rounded-lg bg-black/60 px-3 py-2 text-xs text-white">
            <div>左键拖动旋转</div>
            <div>滚轮缩放</div>
            <div>右键平移</div>
        </div>
    </div>
</template>
