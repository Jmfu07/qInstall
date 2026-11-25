---
name: frontend-design-craft
description: >-
  Mejores prácticas de diseño frontend moderno para interfaces de escritorio WebView2 y web:
  jerarquía visual, contraste accesible, micro-interacciones fluidas, diseño responsivo,
  rendimiento de renderizado y calidad estética de producto real.
---

# Frontend Design Craft Skill

Aplica estos principios al diseñar, refinar o extender cualquier interfaz HTML/CSS/JS (especialmente apps de escritorio en WebView2):

## 1. Jerarquía Visual y Composición
- **Superficies en capas (Depth Scale)**: Diferenciar claramente fondo base (`--bg-base`), tarjetas (`--bg-surface-1`), controles (`--bg-surface-2`) y estados hover/focus (`--bg-surface-3`) con bordes sutiles de 1 px (`rgba(255,255,255,0.07)` en oscuro, `rgba(15,23,42,0.12)` en claro).
- **Una sola acción primaria dominante**: El CTA principal (`Instalar seleccionadas`) debe destacar claramente por color y elevación sobre acciones secundarias (`Descargar offline`, `Ver Registro`).
- **Alineación y ritmo**: Mantener alturas de controles consistentes (36–38 px en toolbar, 40–42 px en footer, 22 px en badges) y tipografía monoespaciada para versiones, porcentajes, contadores y logs.

## 2. Contraste y Temas Reales
- Verificar contraste legible (≥ 4.5:1) en **todos los estados** (normal, hover, seleccionado, deshabilitado/ocupado, modo claro y modo oscuro).
- Nunca depender de estilos de controles deshabilitados por defecto del SO cuando se usa tema oscuro; definir explícitamente opacidad y color de texto legible.
- En modo claro, evitar blancos puros (`#ffffff`) como fondo de página completa; usar grises fríos descansados (`#d8dce6` / `#eaedf5`) con texto grafito oscuro (`#0f172a`).

## 3. Micro-interacciones y Feedback Instantáneo
- Transiciones rápidas y precisas (`160ms–280ms cubic-bezier(0.16, 1, 0.3, 1)`), nunca lentas ni gomosas.
- Todo clic debe dar feedback inmediato: estado activo en botones/chips, toast discreto al guardar o copiar, barra de progreso fluida con texto de estado en tiempo real.
- Atajos de teclado intuitivos (`/` para enfocar búsqueda, `Esc` para cerrar modales o limpiar búsqueda, `Enter` para ejecutar búsqueda instantánea).

## 4. Rendimiento y Cero Bloat
- Sin frameworks pesados ni dependencias de compilación cuando HTML/CSS/JS nativo ofrece mejor velocidad y menor uso de memoria.
- Debounce en búsquedas asíncronas (`350–400ms`) con número de secuencia (`seq`) para descartar respuestas obsoletas.
- Delegación de eventos y `DocumentFragment` al renderizar listas grandes (70+ elementos) para mantener 60 FPS incluso en PCs modestas.
- Adaptación automática a ventanas pequeñas (modo compacto automático en `max-height: 820px` o `max-width: 980px`).
