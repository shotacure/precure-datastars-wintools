-- 人物の姓・名・氏名と、名義の名前・表示用の上書き表記を、文字コードどおりに比べる照合順序（utf8mb4_0900_bin）にする。
-- 日本語用の照合順序（utf8mb4_ja_0900_as_cs_ks）は異体字セレクタ（U+E0100〜 など）を比べるときに無視するため、
-- 「真田 竹志」と、画面の字形どおりに異体字セレクタを付けた名義が同じ名前とみなされる。これを別の名前として扱う。
-- 列の型・NULL の可否・既定値は変えない。何度流しても同じ結果になる。
ALTER TABLE `persons`
  MODIFY COLUMN `family_name` varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin DEFAULT NULL,
  MODIFY COLUMN `given_name`  varchar(64)  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin DEFAULT NULL,
  MODIFY COLUMN `full_name`   varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin NOT NULL;

ALTER TABLE `person_aliases`
  MODIFY COLUMN `name`                  varchar(128)  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin NOT NULL,
  MODIFY COLUMN `display_text_override` varchar(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin DEFAULT NULL;
