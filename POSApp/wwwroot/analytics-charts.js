// RetailPOS - Visual Analytics & Interactive Charting Engine
(function () {
    const instances = {};

    function formatNumber(num) {
        return Number(num || 0).toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

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
        '#64748b'  // Slate
    ];

    const engine = {
        instances: instances,

        destroy: function (canvasId) {
            if (instances[canvasId]) {
                try {
                    instances[canvasId].destroy();
                } catch (e) {
                    console.warn('Error destroying chart', canvasId, e);
                }
                delete instances[canvasId];
            }
        },

        renderDonut: function (canvasId, labels, data, currency, customColors) {
            const canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;

            this.destroy(canvasId);

            const colors = customColors && customColors.length > 0 ? customColors : defaultPalette;
            const ctx = canvas.getContext('2d');

            instances[canvasId] = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: labels,
                    datasets: [{
                        data: data,
                        backgroundColor: colors.slice(0, data.length),
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
                                    return ` ${context.label}: ${formatNumber(val)} ${currency || 'BDT'} (${pct}%)`;
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
            const canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;

            this.destroy(canvasId);

            const ctx = canvas.getContext('2d');
            const processedDatasets = datasets.map((ds, index) => ({
                label: ds.label || 'Value',
                data: ds.data || [],
                backgroundColor: ds.color || defaultPalette[index % defaultPalette.length],
                borderRadius: 6,
                borderSkipped: false,
                maxBarThickness: 36
            }));

            instances[canvasId] = new Chart(ctx, {
                type: isHorizontal ? 'bar' : 'bar',
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
                                    return ` ${context.dataset.label}: ${formatNumber(val)} ${currency || 'BDT'}`;
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { color: isHorizontal ? 'rgba(226, 232, 240, 0.7)' : 'transparent' },
                            ticks: {
                                font: { size: 10 },
                                color: '#64748b',
                                callback: function (val) {
                                    return isHorizontal ? formatNumber(val) : this.getLabelForValue(val);
                                }
                            }
                        },
                        y: {
                            grid: { color: isHorizontal ? 'transparent' : 'rgba(226, 232, 240, 0.7)' },
                            ticks: {
                                font: { size: 10 },
                                color: '#64748b',
                                callback: function (val) {
                                    return isHorizontal ? this.getLabelForValue(val) : formatNumber(val);
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
            const canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;

            this.destroy(canvasId);

            const ctx = canvas.getContext('2d');
            const processedDatasets = datasets.map((ds, index) => {
                const color = ds.color || defaultPalette[index % defaultPalette.length];
                return {
                    label: ds.label || 'Value',
                    data: ds.data || [],
                    borderColor: color,
                    backgroundColor: color + '15',
                    fill: ds.fill !== false,
                    tension: 0.35,
                    borderWidth: 2.5,
                    pointRadius: 3,
                    pointHoverRadius: 6,
                    pointBackgroundColor: '#ffffff',
                    pointBorderColor: color,
                    pointBorderWidth: 2
                };
            });

            instances[canvasId] = new Chart(ctx, {
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
                                    return ` ${context.dataset.label}: ${formatNumber(val)} ${currency || 'BDT'}`;
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
            const chartElements = document.querySelectorAll('canvas[data-chart]');
            chartElements.forEach(canvas => {
                const type = canvas.dataset.chart;
                const canvasId = canvas.id;
                if (!canvasId) return;

                try {
                    const labels = JSON.parse(canvas.dataset.labels || '[]');
                    const currency = canvas.dataset.currency || 'BDT';

                    if (type === 'donut') {
                        const data = JSON.parse(canvas.dataset.values || '[]');
                        const colors = canvas.dataset.colors ? JSON.parse(canvas.dataset.colors) : null;
                        engine.renderDonut(canvasId, labels, data, currency, colors);
                    } else if (type === 'bar') {
                        const datasets = JSON.parse(canvas.dataset.datasets || '[]');
                        const isHorizontal = canvas.dataset.horizontal === 'true';
                        engine.renderBar(canvasId, labels, datasets, currency, isHorizontal);
                    } else if (type === 'line') {
                        const datasets = JSON.parse(canvas.dataset.datasets || '[]');
                        engine.renderLine(canvasId, labels, datasets, currency);
                    }
                } catch (e) {
                    console.error('Failed to parse chart data for', canvasId, e);
                }
            });
        }
    };

    window.PosCharts = engine;
    window.JASCharts = engine; // Backward compatibility alias

    // Auto-init on page load and Blazor enhanced navigation
    if (typeof window !== 'undefined') {
        window.addEventListener('DOMContentLoaded', () => {
            setTimeout(engine.initDeclarativeCharts, 50);
        });

        if (window.Blazor) {
            window.Blazor.addEventListener('enhancedload', () => {
                setTimeout(engine.initDeclarativeCharts, 50);
            });
        }
    }
})();
