<template>
    <div class="relative min-w-0">
        <label v-if="label" class="mb-1 block text-sm">
            {{ label }}
        </label>
        <div
            ref="excludeClickOutside"
            class="border-accent flex min-w-0 cursor-pointer items-center justify-between rounded-md border"
            :class="size === 'sm' ? 'h-6 px-1.5 py-0.5 text-xs' : 'h-12 px-4 py-2'"
            @click="toggleDropdown">
            <span v-if="!selected" class="min-w-0 truncate text-gray-400">{{ placeholder }}</span>
            <span v-else-if="size === 'sm'" class="min-w-0 flex-1 truncate">
                {{ displaySelectedLabel }}
            </span>
            <div v-else class="flex max-h-12 min-w-0 flex-1 flex-wrap gap-2 overflow-auto">
                <span
                    class="bg-accent flex cursor-pointer items-center rounded-md px-3 py-1 text-sm font-medium">
                    <span class="text-accent-content mr-2">{{ displaySelectedLabel }}</span>
                </span>
            </div>
            <div class="flex shrink-0 items-center">
                <LoaderCircleIcon v-if="isLoading" class="mr-2 h-4 w-4 animate-spin" />
                <CaretRightIcon
                    class="arrow-right transition-transform duration-200"
                    :class="[{ 'rotate-90': isOpen }, size === 'sm' ? 'h-3 w-3' : 'h-5 w-5']" />
            </div>
        </div>
        <ul
            v-show="isOpen"
            ref="clickOutside"
            class="border-accent bg-primary absolute z-10 mt-1 max-h-60 w-full overflow-auto rounded-md border shadow-lg">
            <li
                v-if="searchable"
                class="border-accent bg-primary sticky top-0 border-b"
                @click.stop>
                <input
                    ref="searchInput"
                    v-model="searchTerm"
                    type="text"
                    class="w-full bg-transparent outline-hidden"
                    :class="size === 'sm' ? 'px-2 py-1 text-xs' : 'px-4 py-2'"
                    :placeholder="searchPlaceholder"
                    @keydown.enter.prevent="commitSearch"
                    @keydown.esc.prevent="closeDropdown" />
            </li>
            <li
                v-if="!filteredOptions.length && !customSearchValue"
                :class="size === 'sm' ? 'p-1.5 text-xs' : 'p-3'">
                {{ noOptions }}
            </li>
            <li
                v-if="customSearchValue"
                class="cursor-pointer"
                :class="[
                    size === 'sm' ? 'px-2 py-1 text-xs' : 'px-4 py-2',
                    { 'bg-accent/20': selected === customSearchValue }
                ]"
                @click="selectCustom">
                Use "{{ customSearchValue }}"
            </li>
            <li
                v-for="(option, index) in filteredOptions"
                :key="`${option.value}-${index}`"
                class="cursor-pointer"
                :class="[
                    size === 'sm' ? 'px-2 py-1 text-xs' : 'px-4 py-2',
                    { 'bg-accent/20': isSelected(option.value) }
                ]"
                @click="selectOption(option)">
                {{ option.label }}
            </li>
        </ul>
    </div>
</template>

<script setup lang="ts">
import { Ref, ref, nextTick, computed, watch } from 'vue'
import CaretRightIcon from '@/components/icons/CaretRightIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import useClickOutside from '@/composables/useClickOutside'
import { ISelectOption } from '@/ts'

const props = withDefaults(
    defineProps<{
        label?: string
        options: ISelectOption[]
        selected?: string
        disabled?: boolean
        loadOnOpen?: boolean
        placeholder?: string
        noOptions?: string
        size?: 'sm' | 'md'
        searchable?: boolean
        allowCustom?: boolean
        searchPlaceholder?: string
    }>(),
    {
        label: '',
        options: () => [],
        selected: '',
        placeholder: 'Select items...',
        noOptions: 'Select a source language first.',
        size: 'md',
        searchable: false,
        allowCustom: false,
        searchPlaceholder: 'Search...'
    }
)

const emit = defineEmits(['update:selected', 'fetch-options'])

const isOpen: Ref<boolean> = ref(false)
const isLoading: Ref<boolean> = ref(false)
const searchTerm = ref('')
const searchInput: Ref<HTMLInputElement | undefined> = ref()
const clickOutside: Ref<HTMLElement | undefined> = ref()
const excludeClickOutside: Ref<HTMLElement | undefined> = ref()

const sortedOptions = computed(() => {
    return [...props.options].sort((a, b) => a.label.localeCompare(b.label))
})

const filteredOptions = computed(() => {
    const query = searchTerm.value.trim().toLowerCase()
    if (!query) {
        return sortedOptions.value
    }

    return sortedOptions.value.filter(
        (option) =>
            option.label.toLowerCase().includes(query) || option.value.toLowerCase().includes(query)
    )
})

const customSearchValue = computed(() => {
    if (!props.allowCustom) {
        return null
    }

    const query = searchTerm.value.trim()
    if (!query) {
        return null
    }

    const exists = sortedOptions.value.some(
        (option) => option.value === query || option.label.toLowerCase() === query.toLowerCase()
    )
    return exists ? null : query
})

const displaySelectedLabel = computed(() => {
    const option = props.options.find((item) => item.value === props.selected)
    return option ? option.label : props.selected
})

watch(isOpen, async (open) => {
    if (!open) {
        searchTerm.value = ''
        return
    }

    if (!props.searchable) {
        return
    }

    await nextTick()
    searchInput.value?.focus()
})

const closeDropdown = () => {
    isOpen.value = false
}

const toggleDropdown = async () => {
    if (props.disabled) return

    isOpen.value = !isOpen.value

    if (isOpen.value && props.loadOnOpen) {
        isLoading.value = true
        emit('fetch-options')
    }

    await nextTick()
}

const setLoadingState = async (loading: boolean) => {
    isLoading.value = loading
}

const selectOption = (option: ISelectOption) => {
    emit('update:selected', option.value)
    closeDropdown()
}

const selectCustom = () => {
    const value = customSearchValue.value
    if (!value) {
        return
    }

    emit('update:selected', value)
    closeDropdown()
}

const commitSearch = () => {
    if (customSearchValue.value) {
        selectCustom()
        return
    }

    if (filteredOptions.value.length === 1) {
        selectOption(filteredOptions.value[0])
    }
}

const isSelected = (option: string) => {
    return props.selected == option
}

useClickOutside(
    clickOutside,
    () => {
        closeDropdown()
    },
    excludeClickOutside
)

defineExpose({
    setLoadingState
})
</script>
