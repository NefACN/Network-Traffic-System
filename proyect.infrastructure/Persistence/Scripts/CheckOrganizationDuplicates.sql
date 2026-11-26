-- Solo lectura. Debe devolver cero filas antes de aplicar EstructuraOrganizacionalActiva.
-- No elimina ni renombra unidades: las referencias y el historial se conservan.
SELECT upper(btrim(nombre)) AS nombre_normalizado, count(*) AS cantidad,
       array_agg(id_area ORDER BY id_area) AS ids
FROM areas_operativas
GROUP BY upper(btrim(nombre))
HAVING count(*) > 1;

SELECT upper(btrim(nombre)) AS nombre_normalizado, id_ciudad, count(*) AS cantidad,
       array_agg(id_sucursal ORDER BY id_sucursal) AS ids
FROM sucursales
GROUP BY upper(btrim(nombre)), id_ciudad
HAVING count(*) > 1;
