import { mount } from 'svelte';
import App from './App.svelte';
import 'carbon-components-svelte/css/all.css';
import './app.css';
import { initializeTheme } from './themes';

initializeTheme();

mount(App, { target: document.getElementById('app')! });
