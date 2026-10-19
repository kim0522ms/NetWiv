<script setup>
import { ref } from 'vue';
import { commands } from './generated/commands.js';
const name = ref('World');
const message = ref('');
async function greet() {
  try { const result = await commands.greet({ name: name.value }); message.value = result.message + ' (#' + result.count + ')'; }
  catch (error) { message.value = String(error); }
}
</script>
<template><main><h1>NetWiv + Vue</h1><form @submit.prevent="greet"><input v-model="name" aria-label="Name" required><button>Greet</button></form><output>{{ message }}</output></main></template>
