# EmberTrace

EmberTrace — in-process tracer/profiler для .NET, ориентированный на быстрый hot path (Begin/End) без аллокаций и без глобальных lock’ов.  
Сбор данных происходит в thread-local буферы, вся тяжёлая обработка — оффлайн (после остановки сессии).