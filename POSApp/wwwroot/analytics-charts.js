// RetailPOS - Visual Analytics & Interactive Charting Engine
// Full Blazor Enhanced Navigation, StreamRendering, and Static SSR support

const defaultPalette = [
    '#4f46e5', // Electric Indigo
    '#3b82f6', // Bright Blue
    '#10b981', // Emerald Growth
    '#f59e0b', // Amber
    '#8b5cf6', // Violet
    '#ec4899', // Pink
    '#06b6d4', // Cyan
    '#14b8a6', // Teal
    '#f97316', // Orange
    '#6366f1'  // Indigo Accent
];

function formatNumber(num) {
    return Number(num || 0).toLocaleString('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2
    });
}

function resolveColors(count, customColors) {
    const base = customColors && customColors.length > 0 ? customColors : defaultPalette;
    const result = [];
    for (let i = 0; i < count; i++) {
        result.push(base[i % base.length]);
    }
    return result;
}

const instances = {};

export const engine = {
    instances: instances,

    destroy: function (canvasId) {
        if (instances[canvasId]) {
            try {
                instances[canvasId].destroy();
            } catch (e) {
                console.warn('Error destroying chart instance', canvasId, e);
            }
            delete instances[canvasId];
        }

        if (typeof Chart !== 'undefined') {
            const existing = Chart.getChart(canvasId);
            if (existing) {
                try {
                    existing.destroy();
                } catch (e) {
                    console.warn('Error destroying existing Chart.js instance', canvasId, e);
                }
            }
        }
    },

    renderDonut: function (canvasId, labels, data, currency, customColors) {
        const canvas = typeof canvasId === 'string' ? document.getElementById(canvasId) : canvasId;
        if (!canvas || typeof Chart === 'undefined') return;
        const id = canvas.id || canvasId;

        this.destroy(id);

        const colors = resolveColors(data.length, customColors);
        const ctx = canvas.getContext('2d');

        instances[id] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: colors,
                    borderWidth: 2,
                    borderColor: '#ffffff',
                    hoverOffset: 6
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '72%',
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: {
                            boxWidth: 12,
                            boxHeight: 12,
                            padding: 14,
                            font: { size: 11, weight: '500' },
                            color: '#475569'
                        }
                    },
                    tooltip: {
                        backgroundColor: '#0f172a',
                        titleColor: '#f8fafc',
                        bodyColor: '#e2e8f0',
                        padding: 10,
                        boxPadding: 4,
                        cornerRadius: 8,
                        callbacks: {
                            label: function (context) {
                                const val = Number(context.raw) || 0;
                                const total = context.dataset.data.reduce((a, b) => a + (Number(b) || 0), 0);
                                const pct = total > 0 ? ((val / total) * 100).toFixed(1) : 0;
                                const suffix = currency ? ` ${currency}` : '';
                                return ` ${context.label}: ${formatNumber(val)}${suffix} (${pct}%)`;
                            }
                        }
                    }
                },
                animation: {
                    duration: 650,
                    easing: 'easeOutQuart'
                }
            }
        });
    },

    renderBar: function (canvasId, labels, datasets, currency, isHorizontal) {
        const canvas = typeof canvasId === 'string' ? document.getElementById(canvasId) : canvasId;
        if (!canvas || typeof Chart === 'undefined') return;
        const id = canvas.id || canvasId;

        this.destroy(id);

        const ctx = canvas.getContext('2d');
        const processedDatasets = datasets.map((ds, index) => ({
            label: ds.label || 'Value',
            data: ds.data || [],
            backgroundColor: ds.color || defaultPalette[index % defaultPalette.length],
            borderRadius: 6,
            borderSkipped: false,
            maxBarThickness: 36
        }));

        instances[id] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: processedDatasets
            },
            options: {
                indexAxis: isHorizontal ? 'y' : 'x',
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: processedDatasets.length > 1,
                        position: 'top',
                        labels: {
                            boxWidth: 12,
                            boxHeight: 12,
                            padding: 12,
                            font: { size: 11, weight: '500' },
                            color: '#475569'
                        }
                    },
                    tooltip: {
                        backgroundColor: '#0f172a',
                        titleColor: '#f8fafc',
                        bodyColor: '#e2e8f0',
                        padding: 10,
                        boxPadding: 4,
                        cornerRadius: 8,
                        callbacks: {
                            label: function (context) {
                                const val = Number(context.raw) || 0;
                                const suffix = currency ? ` ${currency}` : '';
                                return ` ${context.dataset.label}: ${formatNumber(val)}${suffix}`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        grid: {
                            display: isHorizontal,
                            color: 'rgba(226, 232, 240, 0.7)'
                        },
                        ticks: {
                            font: { size: 10 },
                            color: '#64748b',
                            callback: function (val) {
                                return isHorizontal ? formatNumber(val) : this.getLabelForValue(val);
                            }
                        }
                    },
                    y: {
                        grid: {
                            display: !isHorizontal,
                            color: 'rgba(226, 232, 240, 0.7)'
                        },
                        ticks: {
                            font: { size: 10 },
                            color: '#64748b',
                            callback: function (val) {
                                return !isHorizontal ? formatNumber(val) : this.getLabelForValue(val);
                            }
                        }
                    }
                },
                animation: {
                    duration: 650,
                    easing: 'easeOutQuart'
                }
            }
        });
    },

    renderLine: function (canvasId, labels, datasets, currency) {
        const canvas = typeof canvasId === 'string' ? document.getElementById(canvasId) : canvasId;
        if (!canvas || typeof Chart === 'undefined') return;
        const id = canvas.id || canvasId;

        this.destroy(id);

        const ctx = canvas.getContext('2d');
        const processedDatasets = datasets.map((ds, index) => {
            const color = ds.color || defaultPalette[index % defaultPalette.length];
            return {
                label: ds.label || 'Value',
                data: ds.data || [],
                borderColor: color,
                backgroundColor: color + '1a', // 10% opacity area fill
                fill: true,
                tension: 0.35,
                borderWidth: 2.5,
                pointRadius: 3,
                pointHoverRadius: 6,
                pointBackgroundColor: '#ffffff',
                pointBorderColor: color,
                pointBorderWidth: 2
            };
        });

        instances[id] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: processedDatasets
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: {
                    mode: 'index',
                    intersect: false
                },
                plugins: {
                    legend: {
                        display: processedDatasets.length > 1,
                        position: 'top',
                        labels: {
                            boxWidth: 12,
                            boxHeight: 12,
                            padding: 12,
                            font: { size: 11, weight: '500' },
                            color: '#475569'
                        }
                    },
                    tooltip: {
                        backgroundColor: '#0f172a',
                        titleColor: '#f8fafc',
                        bodyColor: '#e2e8f0',
                        padding: 10,
                        boxPadding: 4,
                        cornerRadius: 8,
                        callbacks: {
                            label: function (context) {
                                const val = Number(context.raw) || 0;
                                const suffix = currency ? ` ${currency}` : '';
                                return ` ${context.dataset.label}: ${formatNumber(val)}${suffix}`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { color: 'transparent' },
                        ticks: { font: { size: 10 }, color: '#64748b' }
                    },
                    y: {
                        grid: { color: 'rgba(226, 232, 240, 0.7)' },
                        ticks: {
                            font: { size: 10 },
                            color: '#64748b',
                            callback: function (val) {
                                return formatNumber(val);
                            }
                        }
                    }
                },
                animation: {
                    duration: 650,
                    easing: 'easeOutQuart'
                }
            }
        });
    },

    initDeclarativeCharts: function () {
        if (typeof Chart === 'undefined') {
            setTimeout(engine.initDeclarativeCharts, 60);
            return;
        }

        const chartElements = document.querySelectorAll('canvas[data-chart]');
        chartElements.forEach(canvas => {
            const type = canvas.dataset.chart;
            const canvasId = canvas.id;
            if (!canvasId) return;

            const payloadKey = `${type}|${canvas.dataset.labels || ''}|${canvas.dataset.values || ''}|${canvas.dataset.datasets || ''}|${canvas.dataset.colors || ''}|${canvas.dataset.horizontal || ''}`;
            if (canvas._lastPayload === payloadKey && Chart.getChart(canvas)) {
                return; // Up to date
            }

            try {
                const labels = JSON.parse(canvas.dataset.labels || '[]');
                const currency = canvas.dataset.currency || '';

                if (type === 'donut') {
                    const data = JSON.parse(canvas.dataset.values || '[]');
                    const colors = canvas.dataset.colors ? JSON.parse(canvas.dataset.colors) : null;
                    engine.renderDonut(canvasId, labels, data, currency, colors);
                    canvas._lastPayload = payloadKey;
                } else if (type === 'bar') {
                    const datasets = JSON.parse(canvas.dataset.datasets || '[]');
                    const isHorizontal = canvas.dataset.horizontal === 'true';
                    engine.renderBar(canvasId, labels, datasets, currency, isHorizontal);
                    canvas._lastPayload = payloadKey;
                } else if (type === 'line') {
                    const datasets = JSON.parse(canvas.dataset.datasets || '[]');
                    engine.renderLine(canvasId, labels, datasets, currency);
                    canvas._lastPayload = payloadKey;
                }
            } catch (e) {
                console.error('Failed to parse chart data for', canvasId, e);
            }
        });
    }
};

let scheduleTimer = null;
function scheduleInit() {
    clearTimeout(scheduleTimer);
    scheduleTimer = setTimeout(() => {
        requestAnimationFrame(() => engine.initDeclarativeCharts());
    }, 40);
}

// Initializer function called by Blazor module lifecycle
export function initialize(blazor) {
    if (blazor && typeof blazor.addEventListener === 'function') {
        blazor.addEventListener('enhancedload', scheduleInit);
        blazor.addEventListener('enhancednavigationend', scheduleInit);
    }
}

// Attach globally for inline/razor/JS interop backward compatibility
if (typeof window !== 'undefined') {
    window.PosCharts = engine;
    window.JASCharts = engine;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scheduleInit);
    } else {
        scheduleInit();
    }

    // Dynamic StreamRendering & DOM insertion watcher
    if (typeof MutationObserver !== 'undefined' && document.body) {
        const observer = new MutationObserver((mutations) => {
            for (const m of mutations) {
                if (m.type === 'childList') {
                    for (const n of m.addedNodes) {
                        if (n.nodeType === 1 && (n.matches?.('canvas[data-chart]') || n.querySelector?.('canvas[data-chart]'))) {
                            scheduleInit();
                            return;
                        }
                    }
                }
            }
        });
        observer.observe(document.body, { childList: true, subtree: true });
    }
}

export const PosCharts = engine;
export const JASCharts = engine;
export default engine;
