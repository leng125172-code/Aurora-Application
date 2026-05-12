<script setup lang="ts">
import { DialogClose as RekaDialogClose } from 'reka-ui'
</script>

<template>
    <RekaDialogClose as-child>
        <slot />
    </RekaDialogClose>
</template>
