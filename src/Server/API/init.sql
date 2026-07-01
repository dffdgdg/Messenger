--
-- PostgreSQL database dump
--

-- Dumped from database version 17.5
-- Dumped by pg_dump version 17.5

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: chat_role; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.chat_role AS ENUM (
    'member',
    'admin',
    'owner'
);


--
-- Name: chat_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.chat_type AS ENUM (
    'chat',
    'department',
    'contact',
    'department_heads'
);


--
-- Name: system_event_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.system_event_type AS ENUM (
    'chat_created',
    'member_added',
    'member_removed',
    'member_left',
    'role_changed',
    'call_started',
    'call_ended',
    'message_pinned',
    'message_unpinned',
    'chat_avatar_updated'
);


--
-- Name: theme; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.theme AS ENUM (
    'light',
    'dark',
    'system'
);


--
-- Name: user_status_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.user_status_type AS ENUM (
    'online',
    'away',
    'do_not_disturb',
    'Offline',
    'busy'
);


--
-- Name: check_contact_uniqueness(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.check_contact_uniqueness() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_chat_type chat_type;
    v_contact_count integer;
    v_user1 integer;
    v_user2 integer;
BEGIN
    -- Получаем тип создаваемого чата
    SELECT type INTO v_chat_type 
    FROM chats 
    WHERE id = NEW.chat_id;
    
    -- Если это не контакт, пропускаем проверку
    IF v_chat_type != 'contact' THEN
        RETURN NEW;
    END IF;
    
    -- Проверяем, что в чате-контакте ровно 2 участника
    SELECT COUNT(*) INTO v_contact_count
    FROM chat_members
    WHERE chat_id = NEW.chat_id;
    
    IF v_contact_count > 2 THEN
        RAISE EXCEPTION 'Contact chat can have only 2 members';
    END IF;
    
    -- Если в контакте уже есть 2 участника, проверяем уникальность
    IF v_contact_count = 2 THEN
        -- Получаем ID двух пользователей в этом контакте
        SELECT MIN(user_id), MAX(user_id) INTO v_user1, v_user2
        FROM chat_members
        WHERE chat_id = NEW.chat_id
        GROUP BY chat_id;
        
        -- Проверяем, существует ли уже контакт между этими пользователями
        IF EXISTS (
            SELECT 1
            FROM chats c
            JOIN chat_members cm1 ON c.id = cm1.chat_id
            JOIN chat_members cm2 ON c.id = cm2.chat_id
            WHERE c.type = 'contact'
              AND c.id != NEW.chat_id
              AND cm1.user_id = v_user1
              AND cm2.user_id = v_user2
        ) THEN
            RAISE EXCEPTION 'Contact between these users already exists';
        END IF;
    END IF;
    
    RETURN NEW;
END;
$$;


--
-- Name: update_chat_last_message_time(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.update_chat_last_message_time() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE public.chats 
    SET last_message_time = NEW.created_at
    WHERE id = NEW.chat_id;
    
    RETURN NEW;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: chats; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.chats (
    id integer NOT NULL,
    name character varying(100),
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by_id integer,
    last_message_time timestamp without time zone,
    avatar text,
    type public.chat_type,
    show_history_for_new_members boolean DEFAULT true NOT NULL
);


--
-- Name: Chats_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Chats_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Chats_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Chats_Id_seq" OWNED BY public.chats.id;


--
-- Name: departments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.departments (
    id integer NOT NULL,
    name character varying(100) NOT NULL,
    parent_department_id integer,
    chat_id integer,
    head_id integer
);


--
-- Name: Departments_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Departments_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Departments_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Departments_Id_seq" OWNED BY public.departments.id;


--
-- Name: message_files; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.message_files (
    id integer NOT NULL,
    file_name character varying(255) NOT NULL,
    content_type character varying(100) NOT NULL,
    message_id integer NOT NULL,
    path text
);


--
-- Name: MessageFiles_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."MessageFiles_id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: MessageFiles_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."MessageFiles_id_seq" OWNED BY public.message_files.id;


--
-- Name: messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.messages (
    id integer NOT NULL,
    chat_id integer NOT NULL,
    sender_id integer NOT NULL,
    content text,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    edited_at timestamp without time zone,
    is_deleted boolean DEFAULT false NOT NULL,
    reply_to_message_id integer,
    forwarded_from_message_id integer,
    target_user_id integer,
    system_event_type public.system_event_type,
    pinned_at timestamp without time zone,
    pinned_by_user_id integer,
    message_type boolean DEFAULT false NOT NULL
);


--
-- Name: Messages_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Messages_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Messages_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Messages_Id_seq" OWNED BY public.messages.id;


--
-- Name: poll_options; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.poll_options (
    id integer NOT NULL,
    poll_id integer NOT NULL,
    option_text character varying(50) NOT NULL,
    "position" integer NOT NULL
);


--
-- Name: PollOptions_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PollOptions_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PollOptions_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PollOptions_Id_seq" OWNED BY public.poll_options.id;


--
-- Name: poll_votes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.poll_votes (
    id integer NOT NULL,
    poll_id integer NOT NULL,
    option_id integer NOT NULL,
    user_id integer NOT NULL,
    voted_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: PollVotes_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."PollVotes_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: PollVotes_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."PollVotes_Id_seq" OWNED BY public.poll_votes.id;


--
-- Name: polls; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.polls (
    id integer NOT NULL,
    message_id integer NOT NULL,
    is_anonymous boolean DEFAULT true,
    allows_multiple_answers boolean DEFAULT false,
    closes_at timestamp without time zone
);


--
-- Name: Polls_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Polls_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Polls_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Polls_Id_seq" OWNED BY public.polls.id;


--
-- Name: RefreshTokens_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."RefreshTokens_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id integer NOT NULL,
    username character varying(32) NOT NULL,
    name character varying(50),
    password_hash text NOT NULL,
    created_at timestamp without time zone DEFAULT now(),
    last_online timestamp without time zone,
    department_id integer,
    avatar text,
    midname character varying(50),
    surname character varying(50),
    is_banned boolean DEFAULT false NOT NULL,
    status_type public.user_status_type DEFAULT 'online'::public.user_status_type,
    status_expires_at timestamp without time zone
);


--
-- Name: Users_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public."Users_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: Users_Id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public."Users_Id_seq" OWNED BY public.users.id;


--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);


--
-- Name: chat_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.chat_members (
    chat_id integer NOT NULL,
    user_id integer NOT NULL,
    joined_at timestamp without time zone DEFAULT now() NOT NULL,
    role public.chat_role DEFAULT 'member'::public.chat_role,
    notifications_enabled boolean DEFAULT true NOT NULL,
    last_read_message_id integer,
    last_read_at timestamp without time zone
);


--
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refresh_tokens (
    id integer DEFAULT nextval('public."RefreshTokens_Id_seq"'::regclass) NOT NULL,
    user_id integer NOT NULL,
    token_hash character varying(128) NOT NULL,
    jwt_id character varying(64) NOT NULL,
    created_at timestamp without time zone NOT NULL,
    expires_at timestamp without time zone NOT NULL,
    used_at timestamp without time zone,
    revoked_at timestamp without time zone,
    replaced_by_token_id integer,
    family_id character varying(64) NOT NULL
);


--
-- Name: system_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.system_settings (
    key character varying(50) NOT NULL,
    value text NOT NULL
);


--
-- Name: user_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_settings (
    user_id integer NOT NULL,
    theme public.theme DEFAULT 'light'::public.theme,
    notifications_enabled boolean DEFAULT true NOT NULL
);


--
-- Name: voice_messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.voice_messages (
    message_id integer NOT NULL,
    duration_seconds double precision DEFAULT 0 NOT NULL,
    file_path text NOT NULL,
    file_size bigint DEFAULT 0 NOT NULL,
    waveform text
);


--
-- Name: chats id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chats ALTER COLUMN id SET DEFAULT nextval('public."Chats_Id_seq"'::regclass);


--
-- Name: departments id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments ALTER COLUMN id SET DEFAULT nextval('public."Departments_Id_seq"'::regclass);


--
-- Name: message_files id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.message_files ALTER COLUMN id SET DEFAULT nextval('public."MessageFiles_id_seq"'::regclass);


--
-- Name: messages id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages ALTER COLUMN id SET DEFAULT nextval('public."Messages_Id_seq"'::regclass);


--
-- Name: poll_options id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_options ALTER COLUMN id SET DEFAULT nextval('public."PollOptions_Id_seq"'::regclass);


--
-- Name: poll_votes id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_votes ALTER COLUMN id SET DEFAULT nextval('public."PollVotes_Id_seq"'::regclass);


--
-- Name: polls id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.polls ALTER COLUMN id SET DEFAULT nextval('public."Polls_Id_seq"'::regclass);


--
-- Name: users id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users ALTER COLUMN id SET DEFAULT nextval('public."Users_Id_seq"'::regclass);


--
-- Data for Name: __EFMigrationsHistory; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
20260405142247_InitialCreate	10.0.5
20260405144218_InitialSchema	10.0.5
20260524211154_InitialCreate	9.0.0
\.


--
-- Data for Name: chat_members; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.chat_members (chat_id, user_id, joined_at, role, notifications_enabled, last_read_message_id, last_read_at) FROM stdin;
77	7	2026-02-20 19:58:39.34526	member	t	1277	2026-05-12 11:05:30.839561
8	7	2025-11-01 10:55:29.999365	member	t	957	2026-05-12 06:02:04.396226
86	7	2026-05-12 06:07:45.530073	owner	f	\N	\N
74	7	2026-05-12 08:22:05.309373	member	t	1308	2026-05-14 11:20:48.325741
82	21	2026-04-18 16:59:59.161184	owner	f	\N	\N
16	10	2026-03-06 09:39:13.436532	member	t	\N	\N
28	7	2026-05-12 05:13:50.009173	member	t	1337	2026-05-14 11:30:12.786068
68	7	2025-12-21 22:35:05.856404	member	t	1265	2026-05-14 11:31:14.260274
6	7	2025-11-01 10:55:29.999365	member	t	1338	2026-05-14 11:31:28.878038
66	1	2025-12-19 12:51:47.161904	owner	t	1340	2026-05-14 12:29:05.821499
2	11	2025-11-01 10:55:29.999365	member	t	\N	\N
2	12	2025-11-01 10:55:29.999365	member	t	\N	\N
2	13	2025-11-01 10:55:29.999365	member	t	\N	\N
3	14	2025-11-01 10:55:29.999365	member	t	\N	\N
3	15	2025-11-01 10:55:29.999365	member	t	\N	\N
3	16	2025-11-01 10:55:29.999365	member	t	\N	\N
65	1	2025-12-19 11:27:13.689465	member	f	478	2026-03-04 10:36:20.664041
16	12	2026-05-12 00:34:33.584926	member	t	\N	\N
6	9	2025-11-01 10:55:29.999365	member	t	\N	\N
8	10	2025-11-01 10:55:29.999365	member	t	\N	\N
9	12	2025-11-01 10:55:29.999365	member	t	\N	\N
74	1	2026-05-04 21:46:28.682859	member	t	1353	2026-05-18 10:05:40.520888
5	8	2025-11-01 10:55:29.999365	admin	t	\N	\N
16	9	2025-11-01 11:01:45.657834	member	t	\N	\N
77	21	2026-02-20 19:58:39.329523	member	t	796	2026-04-22 19:26:19.801008
16	13	2025-11-01 11:01:45.657834	member	t	\N	\N
5	23	2026-03-06 18:58:57.550575	member	t	\N	\N
17	8	2025-11-01 11:01:45.657834	member	t	\N	\N
17	20	2025-11-01 11:01:45.657834	member	t	\N	\N
17	24	2025-11-01 11:01:45.657834	member	t	\N	\N
77	1	2026-02-20 16:43:53.420636	owner	f	1358	2026-05-23 08:19:07.274689
19	14	2025-11-01 11:01:45.657834	member	t	\N	\N
19	16	2025-11-01 11:01:45.657834	member	t	\N	\N
19	25	2025-11-01 11:01:45.657834	member	t	\N	\N
20	15	2025-11-01 11:01:45.657834	member	t	\N	\N
20	23	2025-11-01 11:01:45.657834	member	t	\N	\N
21	17	2025-11-01 11:01:45.657834	member	t	\N	\N
21	18	2025-11-01 11:01:45.657834	member	t	\N	\N
21	22	2025-11-01 11:01:45.657834	member	t	\N	\N
67	18	2025-12-20 06:50:46.868413	member	t	\N	\N
67	1	2025-12-20 06:50:46.844694	owner	t	\N	\N
3	7	2025-11-01 10:55:29.999365	admin	t	229	2026-03-11 08:27:03.404657
9	7	2025-11-01 10:55:29.999365	member	t	238	2026-03-11 08:27:06.189926
69	1	2025-12-24 10:04:53.422615	owner	f	\N	\N
69	8	2025-12-24 10:04:53.426369	member	f	\N	\N
70	1	2025-12-24 10:04:57.789646	owner	f	\N	\N
70	10	2025-12-24 10:04:57.790198	member	f	\N	\N
71	1	2025-12-24 10:57:58.671241	owner	f	\N	\N
71	9	2025-12-24 10:57:58.676155	member	f	\N	\N
16	19	2025-12-24 14:11:27.770685	member	t	\N	\N
66	14	2025-12-19 12:51:47.174242	member	t	480	2026-03-15 16:03:27.435234
75	1	2025-12-30 20:20:40.843035	owner	f	\N	\N
75	13	2025-12-30 20:20:40.870268	member	f	\N	\N
76	9	2026-01-01 13:16:40.397849	member	f	\N	\N
76	19	2026-01-01 13:16:40.389194	owner	f	\N	\N
18	11	2026-03-19 12:12:56.455619	member	t	\N	\N
80	1	2026-03-25 22:32:40.50209	owner	f	\N	\N
80	11	2026-03-25 22:32:40.51614	member	f	\N	\N
74	8	2026-03-26 16:16:34.858514	member	t	\N	\N
6	8	2026-03-27 13:34:17.186131	member	t	\N	\N
77	23	2026-02-20 19:57:57.082002	admin	t	\N	\N
65	19	2025-12-19 11:27:13.655864	owner	t	377	2026-02-11 20:43:28.251081
77	20	2026-02-20 19:58:39.216602	member	t	\N	\N
83	7	2026-04-25 10:42:28.899441	owner	f	\N	\N
83	10	2026-04-25 10:42:28.90846	member	f	\N	\N
2	7	2025-11-01 10:55:29.999365	admin	t	892	2026-04-25 13:43:15.348125
81	25	2026-04-08 11:34:53.215103	owner	f	\N	\N
77	25	2026-02-20 19:57:49.773448	member	t	585	2026-04-08 11:35:02.216438
68	1	2025-12-21 22:35:05.803085	owner	t	657	2026-04-28 08:52:43.144633
77	12	2026-02-20 19:57:49.909905	member	t	\N	\N
77	10	2026-02-20 19:58:39.239248	member	t	\N	\N
77	8	2026-02-20 19:58:39.255685	member	t	\N	\N
77	19	2026-02-20 19:58:39.26971	member	t	\N	\N
77	18	2026-02-20 19:58:39.28487	member	t	\N	\N
77	14	2026-02-20 19:58:39.299448	member	t	\N	\N
77	15	2026-02-20 19:58:39.314294	member	t	\N	\N
81	1	2026-04-08 11:34:53.217569	member	f	586	2026-04-30 09:08:14.107781
77	17	2026-02-20 19:58:39.360041	member	t	\N	\N
77	22	2026-02-20 19:58:39.374362	member	t	\N	\N
77	13	2026-02-20 19:58:39.418587	member	t	\N	\N
77	24	2026-02-20 19:58:39.46199	member	t	\N	\N
77	16	2026-02-20 19:58:39.477859	member	t	\N	\N
77	3	2026-02-20 19:58:39.493142	member	t	\N	\N
77	11	2026-02-20 20:31:24.472209	member	t	\N	\N
77	9	2026-02-20 20:31:24.655695	member	t	\N	\N
82	1	2026-04-18 16:59:59.209404	member	f	662	2026-04-18 17:00:30.384722
74	20	2026-04-14 13:36:07.841691	member	t	\N	\N
74	12	2026-05-04 21:46:26.163598	member	t	\N	\N
6	11	2026-03-02 11:33:40.313627	admin	t	\N	\N
79	1	2026-03-02 13:58:10.987797	owner	f	\N	\N
79	12	2026-03-02 13:58:11.019844	member	f	\N	\N
6	10	2026-05-08 15:53:16.61676	member	t	\N	\N
88	7	2026-05-12 06:18:13.553416	owner	f	\N	\N
18	21	2026-05-11 10:09:57.221637	member	t	\N	\N
90	7	2026-05-12 06:31:50.983459	owner	f	1230	2026-05-14 11:20:32.555276
84	1	2026-05-11 21:55:26.248129	owner	f	\N	\N
84	23	2026-05-11 21:55:26.29469	member	f	\N	\N
16	1	2026-05-04 21:47:04.455498	member	t	1360	2026-05-18 16:58:01.679826
5	1	2025-10-07 07:34:08.840638	owner	t	1248	2026-05-12 08:14:35.266339
6	1	2025-10-07 07:34:15.927598	owner	t	1356	2026-05-18 16:58:39.217078
\.


--
-- Data for Name: chats; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.chats (id, name, created_at, created_by_id, last_message_time, avatar, type, show_history_for_new_members) FROM stdin;
8	Тестовый	2025-10-21 00:48:19.651738	\N	2026-04-30 22:45:58.331129	\N	chat	t
17	Отдел Руководство2	2025-11-01 10:58:04.327204	1	\N	\N	department	t
18	Отдел Дирекция	2025-11-01 10:58:04.327204	1	2026-04-22 19:30:17.982588	\N	department	t
19	Отдел HR (Отдел кадров)	2025-11-01 10:58:04.327204	1	2025-12-21 00:09:42.378641	\N	department	t
20	Отдел IT-отдел	2025-11-01 10:58:04.327204	1	2026-05-03 03:20:02.98962	\N	department	t
21	Отдел Разработка	2025-11-01 10:58:04.327204	1	\N	\N	department	t
79	\N	2026-03-02 13:58:10.730805	1	\N	\N	contact	t
80	\N	2026-03-25 22:32:40.238689	1	\N	\N	contact	t
22	Отдел Тестирование	2025-11-01 10:58:04.327204	1	\N	\N	department	t
23	Отдел Поддержка	2025-11-01 10:58:04.327204	1	\N	\N	department	t
24	Отдел Продажи	2025-11-01 10:58:04.327204	1	\N	\N	department	t
25	Отдел Маркетинг	2025-11-01 10:58:04.327204	1	\N	\N	department	t
26	Отдел Работа с клиентами	2025-11-01 10:58:04.327204	1	\N	\N	department	t
83	\N	2026-04-25 10:42:28.76719	7	\N	\N	contact	t
84	\N	2026-05-11 21:55:25.760137	1	\N	\N	contact	t
27	Отдел Финансы	2025-11-01 10:58:04.327204	1	\N	\N	department	t
29	Отдел Аналитика	2025-11-01 10:58:04.327204	1	\N	\N	department	t
67	18	2025-12-20 06:50:46.79259	1	\N	\N	contact	t
69	\N	2025-12-24 10:04:53.367566	1	\N	\N	contact	t
70	\N	2025-12-24 10:04:57.782416	1	\N	\N	contact	t
72	Отдел Тестовый	2025-12-24 14:26:59.725586	1	\N	\N	department	t
75	\N	2025-12-30 20:20:40.617115	1	\N	\N	contact	t
76	\N	2026-01-01 13:16:40.3139	19	\N	\N	contact	t
3	Групповой чат 3	2025-08-22 19:05:32.137493	1	2026-04-30 22:45:55.608689	\N	chat	t
9	Общий чат	2025-10-31 18:42:44.460446	1	2026-04-30 22:45:52.475869	\N	chat	t
65	1	2025-12-19 11:27:13.34259	19	2026-03-04 10:36:20.485332	\N	contact	t
71	\N	2025-12-24 10:57:58.592737	1	2026-05-12 11:48:34.922539	\N	contact	t
2	Групповой чат 2	2025-08-22 19:05:31.073046	1	2026-05-14 14:36:47.704532	\N	chat	t
74	Руководители отделов	2025-12-24 16:39:33.823457	\N	2026-05-18 13:05:40.251965	\N	department_heads	t
5	Групповой чат 4	2025-10-07 07:34:08.707722	1	2026-05-12 11:14:44.562972	\N	chat	t
77	Чат	2026-02-20 16:43:53.168506	1	2026-05-18 19:44:29.782142	/uploads/chats/fd6704c8-3e39-4efe-9708-df29a6ad1410.webp	chat	t
16	Отдел Компания	2025-11-01 10:58:04.327204	1	2026-05-18 19:45:09.988564	\N	chat	t
82	\N	2026-04-18 16:59:59.032457	21	2026-05-12 11:46:00.583751	\N	contact	t
6	Групповой чат 5	2025-10-07 07:34:15.915398	1	2026-05-25 07:43:31.133055	\N	chat	t
68	7	2025-12-21 22:35:05.517189	1	2026-05-12 11:46:06.925766	\N	contact	t
66	14	2025-12-19 12:51:47.129606	1	2026-05-18 13:04:34.676945	\N	contact	t
81	\N	2026-04-08 11:34:53.166491	25	2026-05-12 11:46:17.497984	\N	contact	t
90	fff	2026-05-12 06:31:50.882292	7	2026-05-12 07:27:28.519394	\N	chat	t
88	авыв	2026-05-12 06:18:13.466173	7	2026-05-12 07:27:35.141902	\N	chat	t
86	Чатыксыв	2026-05-12 06:07:45.454643	7	2026-05-12 07:27:40.042894	\N	chat	t
28	Отдел Бухгалтерия	2025-11-01 10:58:04.327204	1	2026-05-14 14:29:45.905347	\N	department	t
\.


--
-- Data for Name: departments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.departments (id, name, parent_department_id, chat_id, head_id) FROM stdin;
6	Разработка	5	21	\N
7	Тестирование	5	22	\N
8	Поддержка	5	23	\N
9	Продажи	1	24	\N
10	Маркетинг	9	25	\N
11	Работа с клиентами	9	26	\N
12	Финансы	1	27	\N
14	Аналитика	12	29	\N
4	HR (Отдел кадров)	1	19	\N
15	Тестовый	\N	72	\N
3	Дирекция	14	18	8
2	Руководство2	\N	17	20
5	IT-отдел	1	20	12
1	Компания	\N	16	1
13	Бухгалтерия	14	28	7
\.


--
-- Data for Name: message_files; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.message_files (id, file_name, content_type, message_id, path) FROM stdin;
70	AULA_F75_Setup_v2.0_20240509.zip	application/octet-stream	525	/uploads/chats/5/f50b6161-d7c8-4ccb-b279-5278d24fc21f.zip
72	Архитектура (1).png	image/png	885	/uploads/chats/5/1f8589e8-7e30-4ef5-b120-81bd413ce211.png
79	gemini-3-pro-image-preView-2k (nano-banana-pro)_a_Fix_face_skin..png	image/png	937	/http://10.96.171.12:5274/uploads/chats/1/c8fe9c74-b350-4826-a51e-5c9b15be4b1f.png
80	zapret-discord-youtube-1.9.5.rar	application/octet-stream	1014	/uploads/chats/4/22c5e9a0-9f78-41a8-946e-8c1512b62319.rar
88	Архитектура (1).png	image/png	1247	/uploads/chats/5/1f8589e8-7e30-4ef5-b120-81bd413ce211.png
89	Бланк задания (2).docx	application/msword	1261	/uploads/chats/74/69ece863-d7fb-421e-9063-cb4b6e9076eb.docx
90	Бланк задания (2).docx	application/msword	1267	/uploads/chats/71/766bd927-4787-4450-8eda-37e7b3f3688d.docx
91	Архитектура.png	image/png	1268	/uploads/chats/16/b8e8ef98-f26f-447c-b7e5-188430700e5d.png
92	Архитектура.png	image/png	1269	/uploads/chats/16/b8e8ef98-f26f-447c-b7e5-188430700e5d.png
93	9771da4a-d525-4c1b-8a4e-c089e259d929.webp	image/webp	1274	/uploads/chats/16/4e8e5bee-7c97-4df6-bb0d-72f318ed0f53.webp
94	Бланк задания (2).docx	application/msword	1275	/uploads/chats/16/e8220283-3953-467e-982f-641b8a51414d.docx
95	Бланк задания (2).docx	application/msword	1282	/uploads/chats/16/56bbf318-7f66-41e6-ad9f-53be0a1b824c.docx
96	Бланк задания (2).docx	application/msword	1285	/uploads/chats/16/4d9d5d93-d033-4e7e-b524-13ad90589b60.docx
97	Архитектура.png	image/png	1299	/http://172.16.0.2:5274/uploads/chats/16/b8e8ef98-f26f-447c-b7e5-188430700e5d.png
98	1778417029689-019e11d5-7198-7911-a9f8-5c680935dd76.png	image/png	1330	/uploads/chats/28/ddb8850b-9fbe-46ac-82b8-7b6a54dac0d8.png
25	zapret-discord-youtube-1.9.5.rar	application/octet-stream	390	/uploads/chats/6/5f65ccb1-1fc0-421a-b2cc-29cb7540f07f.rar
26	zapret-discord-youtube-1.9.5.rar	application/octet-stream	390	/uploads/chats/6/4165b770-f08d-45c4-aeac-cbb2768c00c3.rar
27	zapret-discord-youtube-1.9.5.rar	application/octet-stream	390	/uploads/chats/6/5b6299f2-31bc-43d1-87bb-94111d91a2f3.rar
47	Mindmap.png	image/png	448	/uploads/chats/16/29d9e48c-426a-41bb-a4d5-c9a82cbf4f80.png
48	Архитектура.png	image/png	449	/uploads/chats/16/b8e8ef98-f26f-447c-b7e5-188430700e5d.png
49	С4.docx	application/msword	453	/uploads/chats/6/aa22dcaf-8b3b-4ccf-9355-98e2b9cc695a.docx
50	План-график.docx	application/msword	454	/uploads/chats/77/f38ddb7f-de07-4417-a4c4-e8cddd5ba9a2.docx
51	Руководство пользователя.chm	application/octet-stream	455	/uploads/chats/77/bbe50c1f-c6f4-449e-9109-0140d3714275.chm
52	Ссылка на проект.txt	text/plain	456	/uploads/chats/77/4f1d7525-5422-4aa1-8abb-e52b14049567.txt
53	Спецификация.docx	application/msword	457	/uploads/chats/77/c88f785a-f65b-40cb-b581-5e5bc78b99b6.docx
54	Руководство пользователя.chm	application/octet-stream	458	/uploads/chats/77/1b881bfa-0dd2-4215-9b18-a86c66cd3a4f.chm
55	SFE-287-0-0-112-1769068745.zip	application/octet-stream	460	/uploads/chats/77/40962125-65ff-4c42-bfb1-65f212412e62.zip
58	Проект_№1_Консалтинг_в_образовательной_организации.docx	application/msword	479	/uploads/chats/66/5086a2f7-e3a0-4175-817a-865ab93f1937.docx
60	DAF_public_RU.xlsx	application/vnd.ms-excel	482	/uploads/chats/6/850657c9-51d0-408e-b5ca-4f6caeb4ea92.xlsx
63	Bring_me_the_horizion_-_Can_You_Feel_My_Heart_(mp3.pm).mp3	audio/mpeg	500	/uploads/chats/6/dc0f5c21-7991-494a-aaf7-3ad277897bdd.mp3
69	Архитектура.png	image/png	512	/uploads/chats/16/b8e8ef98-f26f-447c-b7e5-188430700e5d.png
64	AULA_F75_Setup_v2.0_20240509.zip	application/octet-stream	507	/uploads/chats/5/f50b6161-d7c8-4ccb-b279-5278d24fc21f.zip
65	AULA_F75_Setup_v2.0_20240509.zip	application/octet-stream	508	/uploads/chats/5/f50b6161-d7c8-4ccb-b279-5278d24fc21f.zip
67	AULA_F75_Setup_v2.0_20240509.zip	application/octet-stream	510	/uploads/chats/5/f50b6161-d7c8-4ccb-b279-5278d24fc21f.zip
68	AULA_F75_Setup_v2.0_20240509.zip	application/octet-stream	511	/uploads/chats/5/f50b6161-d7c8-4ccb-b279-5278d24fc21f.zip
99	Архитектура (5).png	image/png	1357	/uploads/chats/77/2055587e-9064-4bc4-8f8c-f1a489aafa77.png
\.


--
-- Data for Name: messages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.messages (id, chat_id, sender_id, content, created_at, edited_at, is_deleted, reply_to_message_id, forwarded_from_message_id, target_user_id, system_event_type, pinned_at, pinned_by_user_id, message_type) FROM stdin;
1220	90	1	Ок	2026-05-12 09:32:16.601966	\N	f	\N	\N	\N	\N	\N	\N	f
1187	5	1	\N	2026-05-12 08:48:39.99251	2026-05-12 05:48:46.761543	t	\N	1182	\N	\N	\N	\N	f
511	16	1	\N	2026-03-13 18:37:19.735009	2026-05-12 05:30:05.346103	t	\N	\N	\N	\N	\N	\N	f
510	5	1	\N	2026-03-13 18:36:59.782975	\N	f	\N	\N	\N	\N	2026-05-11 05:27:48.842807	1	f
1249	5	1	\N	2026-05-12 11:14:39.181635	2026-05-12 08:14:50.358689	t	\N	\N	\N	\N	\N	\N	f
497	6	1	\N	2026-03-08 16:49:33.519475	\N	f	\N	\N	\N	\N	\N	\N	f
501	6	1	\N	2026-03-09 00:43:44.457675	\N	f	\N	\N	\N	\N	\N	\N	f
1270	74	1	\N	2026-05-12 13:52:13.667359	2026-05-12 10:53:17.93767	t	\N	\N	\N	\N	\N	\N	f
504	74	7	\N	2026-03-11 11:43:21.329403	2026-05-12 08:22:41.070702	t	\N	\N	\N	\N	\N	\N	f
357	65	19	Не понял, поясните	2025-12-24 18:40:38.544269	\N	f	\N	\N	\N	\N	\N	\N	f
424	6	1	Уточню позже	2026-02-24 18:56:14.777481	\N	f	\N	\N	\N	\N	\N	\N	f
516	66	14	\N	2026-03-15 19:03:40.296543	2026-03-15 16:03:47.899111	t	\N	\N	\N	\N	\N	\N	f
517	66	1	\N	2026-03-15 19:04:02.526187	\N	f	\N	\N	\N	\N	\N	\N	f
533	6	1	\N	2026-03-26 09:43:46.420744	2026-03-26 06:43:54.881587	t	\N	\N	\N	\N	\N	\N	f
532	6	1	\N	2026-03-26 09:43:45.902368	2026-03-26 06:43:59.679869	t	\N	\N	\N	\N	\N	\N	f
543	6	1	?	2026-04-01 12:51:32.138879	\N	f	\N	\N	\N	\N	\N	\N	f
425	6	1	Обсудим на встрече	2026-02-25 17:40:19.790709	\N	f	\N	\N	\N	\N	\N	\N	f
451	16	7	Можете пожалуйста скинуть задание	2026-03-01 12:36:25.91399	\N	f	\N	\N	\N	\N	2026-05-13 09:24:55.058014	1	f
427	6	1	Хорошо, принято	2026-02-25 17:40:27.753414	\N	f	\N	\N	\N	\N	\N	\N	f
430	6	7	Жду фидбек	2026-02-26 10:15:08.766952	\N	f	\N	\N	\N	\N	\N	\N	f
434	6	1	Уже в работе	2026-02-26 10:20:52.312139	\N	f	\N	\N	\N	\N	\N	\N	f
437	6	1	Готово, проверьте	2026-02-26 10:20:55.442532	\N	f	\N	\N	\N	\N	\N	\N	f
440	6	1	Давайте синхронизируемся	2026-02-26 10:30:38.27479	\N	f	\N	\N	\N	\N	\N	\N	f
442	6	1	Жду вашего ответа	2026-02-26 10:30:39.399022	\N	f	\N	\N	\N	\N	\N	\N	f
444	6	1	Отлично	2026-02-26 10:30:40.285054	\N	f	\N	\N	\N	\N	\N	\N	f
471	6	1	Обновлено	2026-03-03 19:38:17.364956	\N	f	\N	\N	\N	\N	\N	\N	f
486	5	1	Сделал правки	2026-03-05 22:37:16.474426	\N	f	\N	110	\N	\N	\N	\N	f
490	6	1	Есть новости?	2026-03-08 16:29:29.794554	\N	f	\N	\N	\N	\N	\N	\N	f
491	6	1	На согласовании	2026-03-08 16:29:31.209265	\N	f	\N	\N	\N	\N	\N	\N	f
492	6	1	Тестирую	2026-03-08 16:29:32.383856	\N	f	\N	\N	\N	\N	\N	\N	f
493	6	1	Выкатил в прод	2026-03-08 16:29:33.989657	\N	f	\N	\N	\N	\N	\N	\N	f
494	6	1	Готово к ревью	2026-03-08 16:29:35.022088	\N	f	\N	\N	\N	\N	\N	\N	f
495	6	1	Жду ревью	2026-03-08 16:29:36.313235	\N	f	\N	\N	\N	\N	\N	\N	f
522	6	1	Нужны исходные данные	2026-03-21 16:39:26.805516	\N	f	\N	\N	\N	\N	\N	\N	f
527	6	1	Ок, вижу	2026-03-26 09:43:42.956599	\N	f	\N	\N	\N	\N	\N	\N	f
422	77	1	💪	2026-02-20 19:44:08.371882	\N	f	\N	\N	\N	\N	\N	\N	f
1217	90	1	Отправил	2026-05-12 09:32:00.389705	\N	f	\N	\N	\N	\N	\N	\N	f
454	77	1		2026-03-02 00:12:55.390198	\N	f	\N	\N	\N	\N	\N	\N	f
455	77	1		2026-03-02 00:13:06.145027	\N	f	\N	\N	\N	\N	\N	\N	f
458	77	1		2026-03-02 00:23:08.476293	\N	f	\N	\N	\N	\N	\N	\N	f
513	77	1	авыв	2026-03-13 18:37:52.505337	2026-05-15 20:23:44.6991	f	\N	\N	\N	\N	2026-04-11 14:55:01.47289	1	f
474	6	1	сообщение	2026-03-03 19:44:56.33607	\N	f	\N	\N	\N	\N	\N	\N	f
478	65	1	Здравствуйте	2026-03-04 13:36:20.358648	\N	f	\N	\N	\N	\N	\N	\N	f
482	6	1		2026-03-05 19:24:57.468387	\N	f	\N	\N	\N	\N	\N	\N	f
484	5	1	Ку	2026-03-05 22:37:00.515559	\N	f	\N	\N	\N	\N	\N	\N	f
496	6	1	\N	2026-03-08 16:29:55.970009	2026-05-16 11:54:47.708515	t	\N	\N	\N	\N	\N	\N	f
91	2	7	Создал новую ветку.	2025-10-31 18:26:55.797858	\N	f	\N	\N	\N	\N	\N	\N	f
92	2	19	Отправил отчёт в Confluence.	2025-10-30 17:57:26.745225	\N	f	\N	\N	\N	\N	\N	\N	f
93	3	18	Как дела?	2025-11-01 01:31:44.096892	\N	f	\N	\N	\N	\N	\N	\N	f
1340	66	1	Здравствуйте	2026-05-14 15:28:58.142915	2026-05-18 10:04:22.458557	f	\N	\N	\N	\N	\N	\N	f
97	6	14	Привет!	2025-10-31 05:08:51.615929	\N	f	\N	\N	\N	\N	\N	\N	f
100	5	25	Есть идея по улучшению.	2025-10-30 22:56:17.765229	\N	f	\N	\N	\N	\N	\N	\N	f
102	6	9	Как дела?	2025-10-31 10:18:33.911336	\N	f	\N	\N	\N	\N	\N	\N	f
103	2	7	Готово!	2025-10-31 11:41:04.315527	\N	f	\N	\N	\N	\N	\N	\N	f
104	8	12	Проверил задачу, всё работает.	2025-10-31 03:45:56.49596	\N	f	\N	\N	\N	\N	\N	\N	f
109	3	17	Как дела?	2025-10-31 10:39:03.200566	\N	f	\N	\N	\N	\N	\N	\N	f
110	5	11	Отправил отчёт в Confluence.	2025-11-01 05:38:59.397151	\N	f	\N	\N	\N	\N	\N	\N	f
111	2	17	Как дела?	2025-10-31 18:02:31.504407	\N	f	\N	\N	\N	\N	\N	\N	f
449	16	1		2026-03-01 12:35:09.300743	\N	f	\N	\N	\N	\N	2026-05-11 03:29:23.090796	1	f
112	2	15	Готово!	2025-10-30 14:39:13.096264	\N	f	\N	\N	\N	\N	\N	\N	f
118	6	19	Давай позже созвонимся.	2025-10-31 05:44:51.750912	\N	f	\N	\N	\N	\N	\N	\N	f
119	3	22	Есть идея по улучшению.	2025-11-01 08:35:09.489758	\N	f	\N	\N	\N	\N	\N	\N	f
120	6	12	Создал новую ветку.	2025-11-01 03:23:08.659955	\N	f	\N	\N	\N	\N	\N	\N	f
121	3	16	Обновил дизайн интерфейса.	2025-10-30 15:00:12.267088	\N	f	\N	\N	\N	\N	\N	\N	f
1272	16	1	\N	2026-05-12 13:52:59.634132	2026-05-12 10:53:11.968764	t	\N	\N	\N	\N	\N	\N	f
1271	74	1	\N	2026-05-12 13:52:45.533161	2026-05-12 10:53:15.532279	t	\N	\N	\N	\N	\N	\N	f
1341	77	1	ор	2026-05-14 17:55:01.003929	\N	f	\N	\N	\N	\N	\N	\N	f
126	9	17	Проверил задачу, всё работает.	2025-10-31 00:18:57.144739	\N	f	\N	\N	\N	\N	\N	\N	f
127	8	12	Обновил дизайн интерфейса.	2025-10-31 20:57:16.677739	\N	f	\N	\N	\N	\N	\N	\N	f
129	2	16	Проверил задачу, всё работает.	2025-10-31 20:17:37.634076	\N	f	\N	\N	\N	\N	\N	\N	f
130	8	12	Создал новую ветку.	2025-11-01 02:12:01.66979	\N	f	\N	\N	\N	\N	\N	\N	f
1342	77	1	ло	2026-05-14 17:55:04.628554	\N	f	\N	\N	\N	\N	\N	\N	f
133	6	20	Отправил отчёт в Confluence.	2025-10-31 07:45:35.124038	\N	f	\N	\N	\N	\N	\N	\N	f
1250	5	1	\N	2026-05-12 11:14:41.617751	2026-05-12 08:14:49.087728	t	\N	\N	\N	\N	\N	\N	f
1253	74	1	f	2026-05-12 08:15:36.783742	\N	f	\N	\N	\N	message_pinned	\N	\N	t
426	6	1	Да, согласен	2026-02-25 17:40:21.632799	\N	f	\N	\N	\N	\N	\N	\N	f
139	6	10	Привет!	2025-10-30 17:00:43.059946	\N	f	\N	\N	\N	\N	\N	\N	f
140	2	9	Обновил дизайн интерфейса.	2025-10-31 09:29:09.90081	\N	f	\N	\N	\N	\N	\N	\N	f
141	3	18	Есть идея по улучшению.	2025-10-30 22:31:07.277801	\N	f	\N	\N	\N	\N	\N	\N	f
428	6	1	Есть вопросы по задаче	2026-02-25 17:40:50.808695	\N	f	\N	\N	\N	\N	\N	\N	f
431	6	7	Отправил материалы	2026-02-26 10:15:19.887922	\N	f	\N	\N	\N	\N	\N	\N	f
435	6	1	Спасибо за информацию	2026-02-26 10:20:53.396888	\N	f	\N	\N	\N	\N	\N	\N	f
523	6	1	Данные загружены	2026-03-21 16:39:36.335188	\N	f	\N	\N	\N	\N	\N	\N	f
498	6	1	\N	2026-03-08 16:49:53.301175	\N	f	\N	\N	\N	\N	\N	\N	f
528	6	1	Поправил баг	2026-03-26 09:43:43.607887	\N	f	\N	\N	\N	\N	\N	\N	f
149	2	15	Есть идея по улучшению.	2025-11-01 03:27:17.756369	\N	f	\N	\N	\N	\N	\N	\N	f
150	6	21	Давай позже созвонимся.	2025-11-01 03:03:58.548732	\N	f	\N	\N	\N	\N	\N	\N	f
506	6	1	\N	2026-03-12 08:59:10.981405	2026-03-12 05:59:14.024174	t	\N	501	\N	\N	\N	\N	f
152	9	21	Обновил дизайн интерфейса.	2025-10-30 11:51:43.293062	\N	f	\N	\N	\N	\N	\N	\N	f
512	5	1	\N	2026-03-13 18:37:27.364294	\N	f	\N	449	\N	\N	\N	\N	f
154	3	8	Кто будет на встрече?	2025-11-01 02:14:47.316545	\N	f	\N	\N	\N	\N	\N	\N	f
155	9	12	Создал новую ветку.	2025-10-31 05:48:56.798292	\N	f	\N	\N	\N	\N	\N	\N	f
377	65	1	\N	2026-02-09 18:28:18.508047	2026-03-15 16:02:08.731501	t	\N	\N	\N	\N	\N	\N	f
157	5	21	Как дела?	2025-10-31 18:04:45.515346	\N	f	\N	\N	\N	\N	\N	\N	f
158	2	10	Проверил задачу, всё работает.	2025-10-31 03:39:36.300561	\N	f	\N	\N	\N	\N	\N	\N	f
159	6	18	Кто будет на встрече?	2025-10-31 05:36:04.221154	\N	f	\N	\N	\N	\N	\N	\N	f
518	66	1	Здравствуйте	2026-03-15 19:05:51.061136	\N	f	\N	\N	\N	\N	\N	\N	f
162	6	17	Обновил дизайн интерфейса.	2025-10-31 08:52:34.86678	\N	f	\N	\N	\N	\N	\N	\N	f
164	2	14	Проверил задачу, всё работает.	2025-10-31 14:58:48.912677	\N	f	\N	\N	\N	\N	\N	\N	f
166	9	13	Создал новую ветку.	2025-10-30 17:23:59.981816	\N	f	\N	\N	\N	\N	\N	\N	f
167	9	24	Создал новую ветку.	2025-10-31 04:56:56.902526	\N	f	\N	\N	\N	\N	\N	\N	f
169	9	25	Обновил дизайн интерфейса.	2025-10-30 16:09:41.554704	\N	f	\N	\N	\N	\N	\N	\N	f
172	8	14	Кто будет на встрече?	2025-10-31 04:03:30.414731	\N	f	\N	\N	\N	\N	\N	\N	f
173	5	17	Давай позже созвонимся.	2025-10-30 14:27:33.829151	\N	f	\N	\N	\N	\N	\N	\N	f
175	2	21	Есть идея по улучшению.	2025-10-31 08:07:01.676571	\N	f	\N	\N	\N	\N	\N	\N	f
178	2	17	Как дела?	2025-10-30 16:21:48.533111	\N	f	\N	\N	\N	\N	\N	\N	f
179	2	19	Кто будет на встрече?	2025-10-31 22:19:23.494243	\N	f	\N	\N	\N	\N	\N	\N	f
180	2	13	Как дела?	2025-10-30 23:34:40.112057	\N	f	\N	\N	\N	\N	\N	\N	f
182	5	13	Обновил дизайн интерфейса.	2025-10-30 14:55:31.273838	\N	f	\N	\N	\N	\N	\N	\N	f
183	6	9	Отправил отчёт в Confluence.	2025-11-01 02:08:54.956666	\N	f	\N	\N	\N	\N	\N	\N	f
189	5	9	Есть идея по улучшению.	2025-10-31 15:31:24.26796	\N	f	\N	\N	\N	\N	\N	\N	f
190	2	21	Отправил отчёт в Confluence.	2025-10-31 18:13:04.177175	\N	f	\N	\N	\N	\N	\N	\N	f
194	3	9	Готово!	2025-10-31 17:35:01.698844	\N	f	\N	\N	\N	\N	\N	\N	f
195	5	7	Проверил задачу, всё работает.	2025-10-31 05:51:04.805694	\N	f	\N	\N	\N	\N	\N	\N	f
198	8	8	Проверил задачу, всё работает.	2025-10-30 20:02:31.622589	\N	f	\N	\N	\N	\N	\N	\N	f
202	3	15	Создал новую ветку.	2025-10-31 11:47:00.500744	\N	f	\N	\N	\N	\N	\N	\N	f
1273	74	1	\N	2026-05-12 14:03:13.926144	\N	f	\N	\N	\N	\N	\N	\N	f
448	16	1	\N	2026-03-01 12:34:49.4592	2026-03-01 09:34:53.955567	t	\N	\N	\N	\N	\N	\N	f
456	77	1		2026-03-02 00:13:22.18599	\N	f	\N	\N	\N	\N	\N	\N	f
459	77	1	\N	2026-03-02 00:23:20.299993	2026-03-01 21:24:26.558956	t	\N	\N	\N	\N	\N	\N	f
452	6	1	\N	2026-03-02 00:07:24.654058	2026-03-02 11:41:33.564662	t	\N	\N	\N	\N	\N	\N	f
472	6	1	🎁	2026-03-03 19:40:57.075271	\N	f	\N	\N	\N	\N	\N	\N	f
364	66	1	\N	2026-01-10 15:46:47.028762	2026-03-04 10:36:35.129225	t	\N	\N	\N	\N	\N	\N	f
480	66	1	Здравствуйте	2026-03-04 13:37:06.713978	\N	f	\N	\N	\N	\N	\N	\N	f
356	65	19	Здравствуйте	2025-12-24 16:58:01.142644	\N	f	\N	\N	\N	\N	\N	\N	f
207	2	13	Отправил отчёт в Confluence.	2025-10-31 15:46:13.134464	\N	f	\N	\N	\N	\N	\N	\N	f
208	8	16	Отправил отчёт в Confluence.	2025-10-31 19:37:10.465639	\N	f	\N	\N	\N	\N	\N	\N	f
211	9	7	Обновил дизайн интерфейса.	2025-10-31 15:20:43.278282	\N	f	\N	\N	\N	\N	\N	\N	f
1251	5	1	\N	2026-05-12 11:14:43.336335	2026-05-12 08:14:47.851694	t	\N	\N	\N	\N	\N	\N	f
1254	74	1	\N	2026-05-12 08:15:40.250437	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
214	2	24	Создал новую ветку.	2025-10-31 07:51:38.911651	\N	f	\N	\N	\N	\N	\N	\N	f
499	6	1	\N	2026-03-08 18:08:21.978294	\N	f	\N	\N	\N	\N	\N	\N	f
502	74	7	\N	2026-03-11 11:37:30.251689	2026-05-12 08:22:42.631192	t	\N	\N	\N	\N	\N	\N	f
217	6	17	Отправил отчёт в Confluence.	2025-11-01 02:16:47.457461	\N	f	\N	\N	\N	\N	\N	\N	f
524	71	1	\N	2026-03-21 20:32:35.754772	2026-05-12 08:48:15.551119	t	\N	\N	\N	\N	\N	\N	f
219	6	19	Давай позже созвонимся.	2025-11-01 04:08:07.748417	\N	f	\N	\N	\N	\N	\N	\N	f
220	3	10	Проверил задачу, всё работает.	2025-11-01 09:54:04.367172	\N	f	\N	\N	\N	\N	\N	\N	f
221	9	10	Создал новую ветку.	2025-10-30 14:18:18.283845	\N	f	\N	\N	\N	\N	\N	\N	f
429	6	1	Проверю и отпишусь	2026-02-26 10:14:15.856999	\N	f	\N	\N	\N	\N	\N	\N	f
223	9	17	Привет!	2025-10-30 22:53:52.840014	\N	f	\N	\N	\N	\N	\N	\N	f
224	9	13	Привет!	2025-10-30 21:17:36.546696	\N	f	\N	\N	\N	\N	\N	\N	f
432	6	1	Пришлите пример	2026-02-26 10:20:48.295853	\N	f	\N	\N	\N	\N	\N	\N	f
226	2	14	Кто будет на встрече?	2025-10-31 07:42:14.341314	\N	f	\N	\N	\N	\N	\N	\N	f
227	5	14	Создал новую ветку.	2025-10-31 08:53:50.104269	\N	f	\N	\N	\N	\N	\N	\N	f
228	3	23	Создал новую ветку.	2025-11-01 09:56:55.577053	\N	f	\N	\N	\N	\N	\N	\N	f
229	3	15	Кто будет на встрече?	2025-10-31 05:24:54.025791	\N	f	\N	\N	\N	\N	\N	\N	f
433	6	1	Это срочно?	2026-02-26 10:20:49.518844	\N	f	\N	\N	\N	\N	\N	\N	f
231	2	9	Привет!	2025-11-01 01:39:17.483463	\N	f	\N	\N	\N	\N	\N	\N	f
436	6	1	Понял, выполняю	2026-02-26 10:20:54.483763	\N	f	\N	\N	\N	\N	\N	\N	f
233	8	8	Создал новую ветку.	2025-11-01 00:51:47.463245	\N	f	\N	\N	\N	\N	\N	\N	f
234	5	23	Есть идея по улучшению.	2025-10-31 00:49:32.621617	\N	f	\N	\N	\N	\N	\N	\N	f
235	5	19	Привет!	2025-10-30 11:49:27.928397	\N	f	\N	\N	\N	\N	\N	\N	f
237	2	9	Отправил отчёт в Confluence.	2025-10-30 14:24:40.118073	\N	f	\N	\N	\N	\N	\N	\N	f
238	9	7	Как часто вы используете корпоративный мессенджер в течение рабочего дня?	2025-11-01 11:24:48.415333	\N	f	\N	\N	\N	\N	\N	\N	f
239	2	7	Привет!	2025-11-01 11:32:10.15458	\N	f	\N	\N	\N	\N	\N	\N	f
240	2	7	Что за идея?	2025-11-01 11:32:13.713221	\N	f	\N	\N	\N	\N	\N	\N	f
241	2	7	Когда будет встреча?	2025-11-01 11:32:29.703816	\N	f	\N	\N	\N	\N	\N	\N	f
438	6	1	Какие правки нужны?	2026-02-26 10:30:36.542779	\N	f	\N	\N	\N	\N	\N	\N	f
439	6	1	Ок	2026-02-26 10:30:37.558234	\N	f	\N	\N	\N	\N	\N	\N	f
441	6	1	Добавил комментарий	2026-02-26 10:30:38.874663	\N	f	\N	\N	\N	\N	\N	\N	f
443	6	1	Сделаю до вечера	2026-02-26 10:30:39.865137	\N	f	\N	\N	\N	\N	\N	\N	f
445	6	1	Договорились	2026-02-27 18:16:55.062763	\N	f	\N	\N	\N	\N	\N	\N	f
520	6	7	Отчёт готов	2026-03-21 16:35:33.264872	\N	f	\N	\N	\N	\N	\N	\N	f
548	6	7	На доработке	2026-04-04 08:39:09.753457	\N	f	\N	\N	\N	\N	\N	\N	f
390	6	1		2026-02-12 00:00:43.990108	\N	f	\N	\N	\N	\N	\N	\N	f
550	6	7	Нужны уточнения	2026-04-04 08:39:16.715755	\N	f	\N	\N	\N	\N	\N	\N	f
507	5	1		2026-03-12 09:33:28.178647	\N	f	\N	\N	\N	\N	\N	\N	f
508	6	1	\N	2026-03-13 18:32:24.079608	\N	f	\N	507	\N	\N	\N	\N	f
553	6	1	Деплой прошёл успешно	2026-04-04 08:39:41.988117	\N	f	\N	\N	\N	\N	\N	\N	f
554	6	1	@oleg нужен аппрув	2026-04-04 08:39:46.633249	\N	f	\N	\N	\N	\N	\N	\N	f
555	6	1	Задача закрыта	2026-04-04 08:39:58.91148	\N	f	\N	\N	\N	\N	\N	\N	f
549	6	7	Перенёс на завтра	2026-04-04 08:39:13.022754	\N	f	\N	\N	\N	\N	2026-05-13 09:25:12.99474	1	f
479	66	1	\N	2026-03-04 13:36:52.365106	2026-05-14 12:28:14.083467	t	\N	\N	\N	\N	\N	\N	f
525	66	1	\N	2026-03-26 01:28:48.504602	\N	f	\N	508	\N	\N	\N	\N	f
534	6	1	\N	2026-03-26 09:43:46.874914	2026-03-26 06:43:53.131364	t	\N	\N	\N	\N	\N	\N	f
529	6	1	\N	2026-03-26 09:43:44.202948	2026-03-26 06:44:08.063909	t	\N	\N	\N	\N	\N	\N	f
557	77	1	\N	2026-04-06 19:31:26.529897	2026-05-14 13:10:51.676689	t	\N	\N	\N	\N	\N	\N	f
540	6	1	?	2026-03-31 22:16:40.797008	\N	f	\N	\N	\N	\N	\N	\N	f
541	6	1	?	2026-04-01 12:47:11.770915	\N	f	\N	\N	\N	\N	\N	\N	f
546	6	7	@admin здравствуйте	2026-04-04 08:39:02.02785	\N	f	\N	\N	\N	\N	\N	\N	f
1343	77	1	ол	2026-05-14 17:55:08.210024	\N	f	\N	\N	\N	\N	\N	\N	f
453	6	1		2026-03-02 00:07:38.860088	\N	f	\N	\N	\N	\N	\N	\N	f
457	77	1		2026-03-02 00:22:40.627562	\N	f	\N	\N	\N	\N	\N	\N	f
1344	6	1	пав	2026-05-14 17:55:14.133289	\N	f	\N	\N	\N	\N	\N	\N	f
519	6	1	\N	2026-03-19 11:52:35.944507	2026-05-16 11:54:38.896366	t	\N	\N	\N	\N	\N	\N	f
1274	16	1	 	2026-05-12 14:03:42.679962	\N	f	\N	\N	\N	\N	\N	\N	f
446	16	1	😀	2026-03-01 12:33:40.374811	\N	f	\N	\N	\N	\N	2026-05-13 09:24:52.495117	1	f
1345	16	1	па	2026-05-14 17:55:20.194084	\N	f	\N	\N	\N	\N	\N	\N	f
473	6	1	\N	2026-03-03 19:41:00.594976	2026-05-16 11:54:35.284544	t	\N	\N	\N	\N	\N	\N	f
460	77	1		2026-03-02 00:23:29.198766	\N	f	\N	\N	\N	\N	2026-04-11 14:54:57.553198	1	f
537	6	1	\N	2026-03-27 13:33:25.978788	\N	f	\N	\N	7	role_changed	\N	\N	t
500	6	1	\N	2026-03-09 00:43:17.069529	2026-03-12 05:58:59.6191	t	\N	\N	\N	\N	\N	\N	f
536	6	1	\N	2026-03-26 09:43:47.573093	2026-03-26 06:43:49.950916	t	\N	\N	\N	\N	\N	\N	f
535	6	1	\N	2026-03-26 09:43:47.247855	2026-03-26 06:43:51.366104	t	\N	\N	\N	\N	\N	\N	f
531	6	1	\N	2026-03-26 09:43:45.329231	2026-03-26 06:44:00.83141	t	\N	\N	\N	\N	\N	\N	f
530	6	1	\N	2026-03-26 09:43:44.794972	2026-03-26 06:44:03.242974	t	\N	\N	\N	\N	\N	\N	f
552	6	1	@oleg 	2026-04-04 08:39:35.245711	\N	f	\N	\N	\N	\N	\N	\N	f
569	77	1	@dmitry 	2026-04-08 13:35:10.938196	\N	f	\N	\N	\N	\N	\N	\N	f
598	6	7	@admin 	2026-04-10 09:57:22.969084	\N	f	\N	\N	\N	\N	\N	\N	f
599	6	7	@admin 	2026-04-10 09:57:29.92213	\N	f	\N	\N	\N	\N	\N	\N	f
515	77	1	авы	2026-03-15 12:15:45.305494	\N	f	\N	\N	\N	\N	2026-04-11 14:55:03.533852	1	f
589	77	7	@admin 	2026-04-10 09:52:04.742288	\N	f	\N	\N	\N	\N	2026-04-11 14:55:22.107501	1	f
1252	5	1	\N	2026-05-12 11:14:44.562972	2026-05-12 08:14:46.208655	t	\N	\N	\N	\N	\N	\N	f
423	77	1	Уточните, пожалуйста	2026-02-20 22:58:06.862875	\N	f	\N	\N	\N	\N	\N	\N	f
461	77	1	Проверьте, пожалуйста	2026-03-03 19:30:49.33081	\N	f	\N	\N	\N	\N	2026-04-11 14:54:59.614466	1	f
485	5	1	Да	2026-03-05 22:37:02.170476	\N	f	\N	\N	\N	\N	\N	\N	f
489	6	1	Да, начинаем	2026-03-08 15:17:03.655429	\N	f	472	\N	\N	\N	\N	\N	f
503	6	7	Ок	2026-03-11 11:43:17.615461	\N	f	\N	\N	\N	\N	\N	\N	f
505	5	7	Понял	2026-03-11 11:43:24.399889	\N	f	\N	\N	\N	\N	\N	\N	f
521	6	7	Пришлите макет	2026-03-21 16:35:37.264661	\N	f	\N	\N	\N	\N	\N	\N	f
558	6	1	Готово	2026-04-07 12:59:00.172573	\N	f	\N	\N	\N	\N	\N	\N	f
1191	86	7	\N	2026-05-12 06:07:45.559602	\N	f	\N	\N	\N	chat_created	\N	\N	t
1275	16	1	 	2026-05-12 14:03:54.941975	\N	f	\N	\N	\N	\N	\N	\N	f
1255	74	1	\N	2026-05-12 11:23:06.065265	2026-05-12 08:23:15.902495	t	\N	\N	\N	\N	\N	\N	f
1256	74	1	\N	2026-05-12 11:23:27.416586	\N	f	\N	\N	\N	\N	\N	\N	f
1258	74	7	👍	2026-05-12 08:24:02.735804	\N	f	\N	\N	\N	message_pinned	\N	\N	t
539	77	1	\N	2026-03-27 13:52:42.352319	\N	f	\N	\N	23	role_changed	\N	\N	t
609	77	7	Начинаю задачу	2026-04-10 09:59:49.260924	\N	f	\N	\N	\N	\N	2026-04-15 12:23:01.809538	1	f
644	68	1	Сделал	2026-04-15 10:17:28.714751	\N	f	\N	\N	\N	\N	\N	\N	f
647	77	1	Понял	2026-04-15 10:18:01.036905	\N	f	\N	\N	\N	\N	\N	\N	f
648	77	1	Готово	2026-04-15 15:10:35.7884	\N	f	\N	\N	\N	\N	\N	\N	f
649	77	7	Проверьте	2026-04-15 15:10:41.764268	\N	f	\N	\N	\N	\N	\N	\N	f
650	77	1	Обсудим	2026-04-15 15:22:21.347087	\N	f	\N	\N	\N	\N	\N	\N	f
651	77	1	Запланировал	2026-04-15 15:22:26.494117	\N	f	\N	\N	\N	\N	2026-04-15 12:22:46.691295	7	f
652	77	7	Посмотрю	2026-04-15 15:22:28.999706	\N	f	\N	\N	\N	\N	\N	\N	f
1346	74	1	\N	2026-05-15 09:02:15.295644	2026-05-15 19:04:34.057201	t	\N	\N	\N	\N	\N	\N	f
654	68	1	Ок	2026-04-18 18:02:43.039746	\N	f	\N	\N	\N	\N	\N	\N	f
653	66	1	\N	2026-04-15 16:48:42.903375	\N	f	\N	\N	\N	\N	\N	\N	f
727	6	1	\N	2026-04-20 12:14:04.353637	\N	f	\N	\N	\N	call_started	\N	\N	t
655	68	7	Готово	2026-04-18 18:02:48.548946	\N	f	\N	\N	\N	\N	\N	\N	f
656	68	1	Принято	2026-04-18 18:02:56.386487	\N	f	\N	\N	\N	\N	\N	\N	f
657	68	7	Да	2026-04-18 18:02:59.321465	\N	f	\N	\N	\N	\N	\N	\N	f
658	66	1	Позже	2026-04-18 18:03:14.007257	\N	f	\N	\N	\N	\N	\N	\N	f
662	82	21	Обновил статус	2026-04-18 20:00:02.247024	\N	f	\N	\N	\N	\N	\N	\N	f
852	5	7	Смотрю	2026-04-24 13:22:04.21439	\N	f	\N	\N	\N	\N	\N	\N	f
878	5	7	Ок	2026-04-24 20:16:44.518818	\N	f	875	\N	\N	\N	\N	\N	f
879	5	7	Да	2026-04-24 20:16:52.800686	\N	f	874	\N	\N	\N	\N	\N	f
880	5	7	Готово	2026-04-24 20:17:06.521673	\N	f	852	\N	\N	\N	\N	\N	f
885	5	7	Принял	2026-04-25 01:00:41.461877	\N	f	\N	\N	\N	\N	\N	\N	f
938	82	1	На проверке	2026-04-30 22:25:03.16028	\N	f	\N	\N	\N	\N	\N	\N	f
939	66	1	Тестирую	2026-04-30 22:25:07.203428	\N	f	\N	\N	\N	\N	\N	\N	f
949	5	7	Готово	2026-04-30 22:45:32.003749	\N	f	\N	\N	\N	\N	2026-05-11 05:27:58.700347	1	f
951	2	7	Сделал	2026-04-30 22:45:37.567225	\N	f	\N	\N	\N	\N	\N	\N	f
953	6	7	Ок	2026-04-30 22:45:43.909085	\N	f	\N	\N	\N	\N	\N	\N	f
954	77	7	Жду	2026-04-30 22:45:49.434868	\N	f	\N	\N	\N	\N	\N	\N	f
955	9	7	Согласен	2026-04-30 22:45:52.475869	\N	f	\N	\N	\N	\N	\N	\N	f
804	77	1	\N	2026-04-22 20:01:01.485521	\N	f	\N	\N	\N	call_started	\N	\N	t
892	2	7	Отправил отчёт в Confluence.	2026-04-25 16:43:14.230663	\N	f	\N	\N	\N	\N	\N	\N	f
956	3	7	Да, хорошо	2026-04-30 22:45:55.608689	\N	f	\N	\N	\N	\N	\N	\N	f
957	8	7	Обсудим на дейли	2026-04-30 22:45:58.331129	\N	f	\N	\N	\N	\N	\N	\N	f
849	5	1	\N	2026-04-23 20:26:43.87582	2026-05-18 07:08:21.687273	t	\N	\N	\N	\N	2026-04-24 11:36:35.928005	7	f
972	20	1	\N	2026-05-02 18:20:15.950567	\N	f	\N	\N	\N	\N	\N	\N	f
973	20	1	@tanya 	2026-05-02 22:01:18.041724	\N	f	\N	\N	\N	\N	\N	\N	f
974	20	1	https://www.novsu.ru/	2026-05-02 22:01:33.814313	\N	f	\N	\N	\N	\N	\N	\N	f
875	5	7	А КАК	2026-04-24 17:35:55.60391	\N	f	\N	\N	\N	\N	\N	\N	f
873	5	7	\N	2026-04-24 17:25:40.237615	2026-04-24 14:36:02.467691	t	\N	\N	\N	\N	2026-04-24 14:36:01.250981	7	f
874	5	7	\N	2026-04-24 17:29:07.171917	\N	f	\N	\N	\N	\N	\N	\N	f
937	5	1	\N	2026-04-30 15:50:51.257803	2026-04-30 12:57:57.706436	t	\N	\N	\N	\N	\N	\N	f
1192	86	7	\N	2026-05-12 06:07:45.802708	\N	f	\N	\N	1	member_added	\N	\N	t
1276	77	1	\N	2026-05-12 11:04:31.339662	\N	f	\N	\N	\N	chat_avatar_updated	\N	\N	t
1230	90	1	\N	2026-05-12 07:27:28.519394	\N	f	\N	\N	\N	member_left	\N	\N	t
1257	74	7	👍	2026-05-12 11:23:49.511743	2026-05-12 08:23:56.518762	f	1256	\N	\N	\N	2026-05-14 07:33:27.606254	1	f
488	5	1	Шихов Александр удалил Егоров Роман Станиславович из группы	2026-03-06 20:28:34.507031	\N	f	\N	\N	20	member_removed	\N	\N	t
538	6	1	\N	2026-03-27 13:34:17.239682	\N	f	\N	\N	8	member_added	\N	\N	t
544	77	1	\N	2026-04-01 09:51:42.627434	\N	f	\N	\N	20	role_changed	\N	\N	t
542	77	1	\N	2026-04-01 09:47:37.296695	\N	f	\N	\N	20	role_changed	\N	\N	t
659	77	1	Звонок завершён · 1:39	2026-04-18 15:40:55.694269	\N	f	\N	\N	\N	call_ended	\N	\N	t
660	77	1	Звонок завершён · 0:08	2026-04-18 15:42:42.150224	\N	f	\N	\N	\N	call_ended	\N	\N	t
661	77	1	Звонок завершён · 9:16	2026-04-18 16:05:15.138103	\N	f	\N	\N	\N	call_ended	\N	\N	t
663	77	1	Звонок завершён · 0:14	2026-04-18 17:20:38.846566	\N	f	\N	\N	\N	call_ended	\N	\N	t
664	77	1	Звонок завершён · 0:10	2026-04-18 17:42:18.915397	\N	f	\N	\N	\N	call_ended	\N	\N	t
666	77	1	Звонок завершён · 3:14	2026-04-18 18:07:00.568316	\N	f	\N	\N	\N	call_ended	\N	\N	t
667	77	1	Звонок завершён · 1:56	2026-04-19 07:03:08.952793	\N	f	\N	\N	\N	call_ended	\N	\N	t
668	77	1	Звонок завершён · 0:02	2026-04-19 07:03:17.057058	\N	f	\N	\N	\N	call_ended	\N	\N	t
669	77	1	Звонок завершён · 0:11	2026-04-19 07:12:39.272907	\N	f	\N	\N	\N	call_ended	\N	\N	t
670	77	1	Звонок завершён · 0:04	2026-04-19 07:19:56.510011	\N	f	\N	\N	\N	call_ended	\N	\N	t
671	77	1	Звонок завершён · 0:13	2026-04-19 07:22:23.293701	\N	f	\N	\N	\N	call_ended	\N	\N	t
672	77	1	Звонок завершён · 9:01	2026-04-19 07:32:43.931991	\N	f	\N	\N	\N	call_ended	\N	\N	t
673	77	1	Звонок завершён · 4:04	2026-04-19 07:42:09.167809	\N	f	\N	\N	\N	call_ended	\N	\N	t
674	77	1	Звонок завершён · 0:21	2026-04-19 07:42:31.997403	\N	f	\N	\N	\N	call_ended	\N	\N	t
675	77	1	Звонок завершён · 0:04	2026-04-19 07:53:43.429344	\N	f	\N	\N	\N	call_ended	\N	\N	t
676	77	1	Звонок завершён · 0:28	2026-04-19 07:56:24.11981	\N	f	\N	\N	\N	call_ended	\N	\N	t
677	77	1	Звонок завершён · 0:35	2026-04-19 07:57:01.052937	\N	f	\N	\N	\N	call_ended	\N	\N	t
678	77	1	Звонок завершён · 0:13	2026-04-19 08:02:46.857953	\N	f	\N	\N	\N	call_ended	\N	\N	t
679	77	1	Звонок завершён · 0:55	2026-04-19 08:03:58.226208	\N	f	\N	\N	\N	call_ended	\N	\N	t
680	77	1	Звонок завершён · 0:26	2026-04-19 08:12:15.282582	\N	f	\N	\N	\N	call_ended	\N	\N	t
681	77	1	Звонок завершён · 0:14	2026-04-19 08:48:45.73959	\N	f	\N	\N	\N	call_ended	\N	\N	t
682	77	1	Звонок завершён · 0:07	2026-04-19 08:53:43.760225	\N	f	\N	\N	\N	call_ended	\N	\N	t
683	77	1	Звонок завершён · 0:18	2026-04-19 09:03:37.810412	\N	f	\N	\N	\N	call_ended	\N	\N	t
684	77	1	Звонок завершён · 0:04	2026-04-19 09:09:13.153536	\N	f	\N	\N	\N	call_ended	\N	\N	t
685	77	1	Звонок завершён · 6:11	2026-04-19 09:18:33.117962	\N	f	\N	\N	\N	call_ended	\N	\N	t
686	77	1	Звонок завершён · 1:31	2026-04-19 11:03:10.679377	\N	f	\N	\N	\N	call_ended	\N	\N	t
687	77	1	Звонок завершён · 0:31	2026-04-19 11:04:41.97832	\N	f	\N	\N	\N	call_ended	\N	\N	t
688	77	1	Звонок завершён · 0:49	2026-04-19 11:35:39.63139	\N	f	\N	\N	\N	call_ended	\N	\N	t
689	77	1	Звонок завершён · 0:20	2026-04-19 11:36:52.566861	\N	f	\N	\N	\N	call_ended	\N	\N	t
690	77	1	Звонок завершён · 4:18	2026-04-19 11:56:12.103647	\N	f	\N	\N	\N	call_ended	\N	\N	t
692	6	1	Звонок завершён · 1:38	2026-04-19 11:57:58.780621	\N	f	\N	\N	\N	call_ended	\N	\N	t
693	6	1	Звонок завершён · 2:11	2026-04-19 12:00:20.376592	\N	f	\N	\N	\N	call_ended	\N	\N	t
694	77	21	Звонок завершён · 2:51	2026-04-19 12:53:39.348524	\N	f	\N	\N	\N	call_ended	\N	\N	t
695	77	1	Звонок завершён · 0:33	2026-04-19 12:57:03.778697	\N	f	\N	\N	\N	call_ended	\N	\N	t
696	77	1	Звонок завершён · 58:11	2026-04-19 13:59:46.915785	\N	f	\N	\N	\N	call_ended	\N	\N	t
697	77	21	Звонок завершён · 0:27	2026-04-19 14:06:56.247489	\N	f	\N	\N	\N	call_ended	\N	\N	t
698	77	21	Звонок завершён · 0:04	2026-04-19 14:07:18.573509	\N	f	\N	\N	\N	call_ended	\N	\N	t
699	77	21	Звонок завершён · 0:49	2026-04-19 14:09:29.724947	\N	f	\N	\N	\N	call_ended	\N	\N	t
700	77	1	Звонок завершён · 1:53	2026-04-19 14:15:32.200206	\N	f	\N	\N	\N	call_ended	\N	\N	t
701	77	1	Звонок завершён · 0:25	2026-04-19 15:47:25.491084	\N	f	\N	\N	\N	call_ended	\N	\N	t
702	77	1	Звонок завершён · 0:44	2026-04-19 15:50:24.879936	\N	f	\N	\N	\N	call_ended	\N	\N	t
703	77	1	Звонок завершён · 0:36	2026-04-19 15:57:49.470694	\N	f	\N	\N	\N	call_ended	\N	\N	t
704	77	1	Звонок завершён · 1:25	2026-04-19 15:59:20.060616	\N	f	\N	\N	\N	call_ended	\N	\N	t
705	77	1	Звонок завершён · 1:37	2026-04-19 16:15:35.908182	\N	f	\N	\N	\N	call_ended	\N	\N	t
706	77	1	Звонок завершён · 0:26	2026-04-19 16:31:12.923375	\N	f	\N	\N	\N	call_ended	\N	\N	t
707	77	1	Звонок завершён · 0:39	2026-04-19 16:32:00.275856	\N	f	\N	\N	\N	call_ended	\N	\N	t
708	77	1	Звонок завершён · 1:04	2026-04-19 16:42:01.789419	\N	f	\N	\N	\N	call_ended	\N	\N	t
709	77	1	Звонок завершён · 1:13	2026-04-19 16:43:49.333318	\N	f	\N	\N	\N	call_ended	\N	\N	t
710	77	1	Звонок завершён · 0:08	2026-04-19 16:45:50.958864	\N	f	\N	\N	\N	call_ended	\N	\N	t
711	77	1	Звонок завершён · 1:45	2026-04-19 16:50:47.452329	\N	f	\N	\N	\N	call_ended	\N	\N	t
712	77	1	Звонок завершён · 0:07	2026-04-19 16:53:13.065873	\N	f	\N	\N	\N	call_ended	\N	\N	t
713	77	1	Звонок завершён · 0:15	2026-04-19 16:55:10.193551	\N	f	\N	\N	\N	call_ended	\N	\N	t
714	77	1	Звонок завершён · 1:34	2026-04-19 17:01:33.833874	\N	f	\N	\N	\N	call_ended	\N	\N	t
715	77	1	Звонок завершён · 0:28	2026-04-19 17:03:13.107694	\N	f	\N	\N	\N	call_ended	\N	\N	t
716	77	1	Звонок завершён · 1:21	2026-04-19 17:05:38.248566	\N	f	\N	\N	\N	call_ended	\N	\N	t
717	77	1	Звонок завершён · 0:30	2026-04-19 18:32:37.016126	\N	f	\N	\N	\N	call_ended	\N	\N	t
718	77	21	Звонок завершён · 0:45	2026-04-19 18:53:09.457902	\N	f	\N	\N	\N	call_ended	\N	\N	t
719	77	1	Звонок завершён · 0:30	2026-04-19 22:23:01.821113	\N	f	\N	\N	\N	call_ended	\N	\N	t
720	77	1	Звонок завершён · 0:25	2026-04-19 22:29:04.014981	\N	f	\N	\N	\N	call_ended	\N	\N	t
721	77	1	Звонок завершён · 1:01	2026-04-19 22:35:15.530725	\N	f	\N	\N	\N	call_ended	\N	\N	t
722	77	1	Звонок завершён · 0:35	2026-04-19 22:40:44.938988	\N	f	\N	\N	\N	call_ended	\N	\N	t
1259	74	7	\N	2026-05-12 08:24:06.050693	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1261	74	1	Задание	2026-05-12 11:25:51.337316	\N	f	\N	\N	\N	\N	\N	\N	f
1260	74	1	\N	2026-05-12 11:24:22.95109	2026-05-12 09:18:44.293722	t	\N	\N	\N	\N	\N	\N	f
723	77	1	Звонок завершён · 5:34	2026-04-20 10:59:06.379785	\N	f	\N	\N	\N	call_ended	\N	\N	t
724	77	1	Звонок завершён · 0:01	2026-04-20 11:49:24.906691	\N	f	\N	\N	\N	call_ended	\N	\N	t
725	77	1	Звонок завершён · 0:01	2026-04-20 12:01:12.970969	\N	f	\N	\N	\N	call_ended	\N	\N	t
726	6	1	Звонок завершён · 0:04	2026-04-20 12:01:23.354364	\N	f	\N	\N	\N	call_ended	\N	\N	t
728	6	1	Звонок завершён · 0:02	2026-04-20 12:14:06.174928	\N	f	\N	\N	\N	call_ended	\N	\N	t
729	6	1	\N	2026-04-20 12:14:08.434877	\N	f	\N	\N	\N	call_started	\N	\N	t
730	6	1	Звонок завершён · 3:10	2026-04-20 12:17:18.767212	\N	f	\N	\N	\N	call_ended	\N	\N	t
731	6	1	\N	2026-04-20 12:23:22.875938	\N	f	\N	\N	\N	call_started	\N	\N	t
732	6	1	Звонок завершён · 0:10	2026-04-20 12:23:32.635116	\N	f	\N	\N	\N	call_ended	\N	\N	t
733	6	1	\N	2026-04-20 12:23:36.044818	\N	f	\N	\N	\N	call_started	\N	\N	t
734	6	1	Звонок завершён · 0:01	2026-04-20 12:23:37.065046	\N	f	\N	\N	\N	call_ended	\N	\N	t
735	77	1	\N	2026-04-20 12:23:39.441559	\N	f	\N	\N	\N	call_started	\N	\N	t
736	77	1	Звонок завершён · 0:01	2026-04-20 12:23:40.858229	\N	f	\N	\N	\N	call_ended	\N	\N	t
1231	88	1	\N	2026-05-12 07:27:35.141902	\N	f	\N	\N	\N	member_left	\N	\N	t
739	5	1	\N	2026-04-20 12:23:49.988439	\N	f	\N	\N	\N	call_started	\N	\N	t
740	5	1	Звонок завершён · 0:02	2026-04-20 12:23:52.4959	\N	f	\N	\N	\N	call_ended	\N	\N	t
741	6	1	\N	2026-04-20 12:24:21.102833	\N	f	\N	\N	\N	call_started	\N	\N	t
742	6	1	Звонок завершён · 0:06	2026-04-20 12:24:27.023192	\N	f	\N	\N	\N	call_ended	\N	\N	t
743	6	1	\N	2026-04-21 08:12:42.087756	\N	f	\N	\N	\N	call_started	\N	\N	t
744	6	1	Звонок завершён · 0:08	2026-04-21 08:12:50.857215	\N	f	\N	\N	\N	call_ended	\N	\N	t
745	6	1	\N	2026-04-22 12:03:53.807124	\N	f	\N	\N	\N	call_started	\N	\N	t
746	6	1	Звонок завершён · 0:05	2026-04-22 12:03:58.795768	\N	f	\N	\N	\N	call_ended	\N	\N	t
747	77	1	\N	2026-04-22 13:13:11.920631	\N	f	\N	\N	\N	call_started	\N	\N	t
748	77	1	Звонок завершён · 1:20	2026-04-22 13:14:32.347401	\N	f	\N	\N	\N	call_ended	\N	\N	t
749	77	7	\N	2026-04-22 13:14:35.15261	\N	f	\N	\N	\N	call_started	\N	\N	t
750	77	7	Звонок завершён · 0:55	2026-04-22 13:15:30.733857	\N	f	\N	\N	\N	call_ended	\N	\N	t
751	6	1	\N	2026-04-22 13:15:30.981677	\N	f	\N	\N	\N	call_started	\N	\N	t
752	6	1	Звонок завершён · 2:05	2026-04-22 13:17:36.743609	\N	f	\N	\N	\N	call_ended	\N	\N	t
753	6	1	\N	2026-04-22 13:17:43.749099	\N	f	\N	\N	\N	call_started	\N	\N	t
754	6	1	Звонок завершён · 0:44	2026-04-22 13:18:28.213112	\N	f	\N	\N	\N	call_ended	\N	\N	t
755	6	1	\N	2026-04-22 13:18:29.5541	\N	f	\N	\N	\N	call_started	\N	\N	t
756	6	1	Звонок завершён · 11:19	2026-04-22 13:29:49.496883	\N	f	\N	\N	\N	call_ended	\N	\N	t
757	77	1	\N	2026-04-22 13:35:47.968447	\N	f	\N	\N	\N	call_started	\N	\N	t
758	77	1	Звонок завершён · 0:30	2026-04-22 13:36:17.856605	\N	f	\N	\N	\N	call_ended	\N	\N	t
759	77	1	\N	2026-04-22 13:45:21.06112	\N	f	\N	\N	\N	call_started	\N	\N	t
760	77	1	Звонок завершён · 0:45	2026-04-22 13:46:05.91143	\N	f	\N	\N	\N	call_ended	\N	\N	t
761	77	1	\N	2026-04-22 13:54:37.802718	\N	f	\N	\N	\N	call_started	\N	\N	t
762	77	1	Звонок завершён · 18:40	2026-04-22 14:13:18.161533	\N	f	\N	\N	\N	call_ended	\N	\N	t
763	77	1	\N	2026-04-22 15:15:38.10584	\N	f	\N	\N	\N	call_started	\N	\N	t
764	77	1	Звонок завершён · 0:06	2026-04-22 15:15:44.59038	\N	f	\N	\N	\N	call_ended	\N	\N	t
765	77	1	\N	2026-04-22 15:20:04.624562	\N	f	\N	\N	\N	call_started	\N	\N	t
766	77	1	Звонок завершён · 1:11	2026-04-22 15:21:15.450017	\N	f	\N	\N	\N	call_ended	\N	\N	t
767	77	1	\N	2026-04-22 15:47:54.118347	\N	f	\N	\N	\N	call_started	\N	\N	t
768	77	1	Звонок завершён · 1:52	2026-04-22 15:49:46.836404	\N	f	\N	\N	\N	call_ended	\N	\N	t
769	77	1	\N	2026-04-22 16:05:13.900981	\N	f	\N	\N	\N	call_started	\N	\N	t
770	77	1	Звонок завершён · 1:09	2026-04-22 16:06:23.663767	\N	f	\N	\N	\N	call_ended	\N	\N	t
771	77	1	\N	2026-04-22 16:06:27.859575	\N	f	\N	\N	\N	call_started	\N	\N	t
772	77	1	Звонок завершён · 1:40	2026-04-22 16:08:08.694603	\N	f	\N	\N	\N	call_ended	\N	\N	t
773	77	1	\N	2026-04-22 16:44:04.446716	\N	f	\N	\N	\N	call_started	\N	\N	t
774	77	1	Звонок завершён · 1:26	2026-04-22 16:45:30.509868	\N	f	\N	\N	\N	call_ended	\N	\N	t
775	77	1	\N	2026-04-22 16:48:05.721868	\N	f	\N	\N	\N	call_started	\N	\N	t
776	77	1	Звонок завершён · 1:17	2026-04-22 16:49:23.367481	\N	f	\N	\N	\N	call_ended	\N	\N	t
777	77	1	\N	2026-04-22 18:28:32.940853	\N	f	\N	\N	\N	call_started	\N	\N	t
778	77	1	\N	2026-04-22 18:50:36.84936	\N	f	\N	\N	\N	call_started	\N	\N	t
779	77	1	Звонок завершён · 0:15	2026-04-22 18:50:52.630193	\N	f	\N	\N	\N	call_ended	\N	\N	t
780	77	1	\N	2026-04-22 19:02:25.480399	\N	f	\N	\N	\N	call_started	\N	\N	t
781	77	1	Звонок завершён · 0:01	2026-04-22 19:02:26.993531	\N	f	\N	\N	\N	call_ended	\N	\N	t
782	77	1	\N	2026-04-22 19:03:26.672259	\N	f	\N	\N	\N	call_started	\N	\N	t
783	77	1	Звонок завершён · 0:33	2026-04-22 19:04:00.375228	\N	f	\N	\N	\N	call_ended	\N	\N	t
784	77	1	\N	2026-04-22 19:07:24.091866	\N	f	\N	\N	\N	call_started	\N	\N	t
785	77	1	Звонок завершён · 3:12	2026-04-22 19:10:36.784411	\N	f	\N	\N	\N	call_ended	\N	\N	t
786	77	1	\N	2026-04-22 19:13:36.497307	\N	f	\N	\N	\N	call_started	\N	\N	t
787	77	1	Звонок завершён · 1:37	2026-04-22 19:15:13.665438	\N	f	\N	\N	\N	call_ended	\N	\N	t
788	77	1	\N	2026-04-22 19:15:56.745268	\N	f	\N	\N	\N	call_started	\N	\N	t
789	77	1	Звонок завершён · 1:13	2026-04-22 19:17:09.929273	\N	f	\N	\N	\N	call_ended	\N	\N	t
790	77	1	\N	2026-04-22 19:17:44.167711	\N	f	\N	\N	\N	call_started	\N	\N	t
791	77	1	Звонок завершён · 1:13	2026-04-22 19:18:57.672467	\N	f	\N	\N	\N	call_ended	\N	\N	t
792	77	1	\N	2026-04-22 19:22:39.873543	\N	f	\N	\N	\N	call_started	\N	\N	t
793	77	1	Звонок завершён · 0:33	2026-04-22 19:23:13.603761	\N	f	\N	\N	\N	call_ended	\N	\N	t
794	77	1	\N	2026-04-22 19:24:16.879317	\N	f	\N	\N	\N	call_started	\N	\N	t
795	77	1	Звонок завершён · 1:58	2026-04-22 19:26:15.337646	\N	f	\N	\N	\N	call_ended	\N	\N	t
796	77	1	\N	2026-04-22 19:26:19.671936	\N	f	\N	\N	\N	call_started	\N	\N	t
797	77	1	Звонок завершён · 1:50	2026-04-22 19:28:09.934742	\N	f	\N	\N	\N	call_ended	\N	\N	t
798	77	21	\N	2026-04-22 19:29:15.931267	\N	f	\N	\N	\N	call_started	\N	\N	t
799	18	21	\N	2026-04-22 19:30:11.375925	\N	f	\N	\N	\N	call_started	\N	\N	t
800	77	21	Звонок завершён · 0:59	2026-04-22 19:30:14.872404	\N	f	\N	\N	\N	call_ended	\N	\N	t
801	77	1	\N	2026-04-22 19:30:15.999868	\N	f	\N	\N	\N	call_started	\N	\N	t
802	18	21	Звонок завершён · 0:06	2026-04-22 19:30:17.982588	\N	f	\N	\N	\N	call_ended	\N	\N	t
803	77	1	Звонок завершён · 0:14	2026-04-22 19:30:30.849894	\N	f	\N	\N	\N	call_ended	\N	\N	t
805	77	1	Звонок завершён · 1:15	2026-04-22 20:02:16.250141	\N	f	\N	\N	\N	call_ended	\N	\N	t
806	77	1	\N	2026-04-22 20:08:43.883491	\N	f	\N	\N	\N	call_started	\N	\N	t
807	77	1	Звонок завершён · 3:50	2026-04-22 20:12:34.301189	\N	f	\N	\N	\N	call_ended	\N	\N	t
808	77	1	\N	2026-04-22 20:33:01.290283	\N	f	\N	\N	\N	call_started	\N	\N	t
1262	74	1	Опрос	2026-05-12 11:26:27.746838	\N	f	\N	\N	\N	\N	\N	\N	f
809	77	1	\N	2026-04-22 20:38:02.476281	\N	f	\N	\N	\N	call_started	\N	\N	t
810	77	1	Звонок завершён · 2:49	2026-04-22 20:40:51.735559	\N	f	\N	\N	\N	call_ended	\N	\N	t
811	77	1	\N	2026-04-22 20:42:52.297437	\N	f	\N	\N	\N	call_started	\N	\N	t
812	77	1	Звонок завершён · 0:17	2026-04-22 20:43:09.920553	\N	f	\N	\N	\N	call_ended	\N	\N	t
813	77	1	\N	2026-04-23 08:03:55.636517	\N	f	\N	\N	\N	call_started	\N	\N	t
814	77	1	Звонок завершён · 0:19	2026-04-23 08:04:14.480735	\N	f	\N	\N	\N	call_ended	\N	\N	t
815	77	1	\N	2026-04-23 08:30:33.265101	\N	f	\N	\N	\N	call_started	\N	\N	t
816	77	1	Звонок завершён · 0:15	2026-04-23 08:30:48.428599	\N	f	\N	\N	\N	call_ended	\N	\N	t
817	77	1	\N	2026-04-23 08:31:49.02623	\N	f	\N	\N	\N	call_started	\N	\N	t
818	77	1	Звонок завершён · 0:15	2026-04-23 08:32:04.410023	\N	f	\N	\N	\N	call_ended	\N	\N	t
819	77	1	\N	2026-04-23 08:35:21.633262	\N	f	\N	\N	\N	call_started	\N	\N	t
820	77	1	Звонок завершён · 0:33	2026-04-23 08:35:54.965112	\N	f	\N	\N	\N	call_ended	\N	\N	t
821	77	1	\N	2026-04-23 08:41:24.747833	\N	f	\N	\N	\N	call_started	\N	\N	t
822	77	1	Звонок завершён · 1:03	2026-04-23 08:42:27.978757	\N	f	\N	\N	\N	call_ended	\N	\N	t
823	77	1	\N	2026-04-23 14:14:08.897617	\N	f	\N	\N	\N	call_started	\N	\N	t
824	77	1	Звонок завершён · 0:03	2026-04-23 14:14:12.212024	\N	f	\N	\N	\N	call_ended	\N	\N	t
825	6	1	\N	2026-04-23 14:14:16.146821	\N	f	\N	\N	\N	call_started	\N	\N	t
826	6	1	Звонок завершён · 0:01	2026-04-23 14:14:18.000465	\N	f	\N	\N	\N	call_ended	\N	\N	t
827	16	1	\N	2026-04-23 14:14:19.980185	\N	f	\N	\N	\N	call_started	\N	\N	t
828	16	1	Звонок завершён · 0:06	2026-04-23 14:14:26.712411	\N	f	\N	\N	\N	call_ended	\N	\N	t
1278	16	1	Жду	2026-05-12 14:04:45.212539	\N	f	\N	954	\N	\N	\N	\N	f
1347	74	1	\N	2026-05-15 14:57:58.399213	2026-05-15 19:04:37.425175	t	\N	\N	\N	\N	\N	\N	f
1232	86	1	\N	2026-05-12 07:27:40.042894	\N	f	\N	\N	\N	member_left	\N	\N	t
1263	66	1	Здравствуйте	2026-05-12 11:45:52.686428	\N	f	\N	\N	\N	\N	\N	\N	f
844	5	1	\N	2026-04-23 16:56:45.390236	\N	f	\N	\N	\N	call_started	\N	\N	t
845	5	1	Звонок завершён · 0:01	2026-04-23 16:56:46.875983	\N	f	\N	\N	\N	call_ended	\N	\N	t
846	5	1	\N	2026-04-23 17:24:55.794871	\N	f	\N	\N	\N	call_started	\N	\N	t
847	5	1	Звонок завершён · 0:13	2026-04-23 17:25:08.516354	\N	f	\N	\N	\N	call_ended	\N	\N	t
857	16	7	\N	2026-04-24 10:23:16.324182	\N	f	\N	\N	\N	call_started	\N	\N	t
867	5	7	\N	2026-04-24 11:31:11.557192	\N	f	\N	\N	\N	call_started	\N	\N	t
868	16	7	\N	2026-04-24 13:17:18.908072	\N	f	\N	\N	\N	call_started	\N	\N	t
869	16	7	Звонок завершён · 0:30	2026-04-24 13:17:49.306607	\N	f	\N	\N	\N	call_ended	\N	\N	t
876	5	7	\N	2026-04-24 17:15:21.634406	\N	f	\N	\N	\N	call_started	\N	\N	t
877	5	7	Звонок завершён · 0:12	2026-04-24 17:15:33.853794	\N	f	\N	\N	\N	call_ended	\N	\N	t
881	5	7	\N	2026-04-24 17:17:28.485491	\N	f	\N	\N	\N	call_started	\N	\N	t
882	5	7	Звонок завершён · 0:01	2026-04-24 17:17:30.273774	\N	f	\N	\N	\N	call_ended	\N	\N	t
883	16	7	\N	2026-04-24 17:20:01.982111	\N	f	\N	\N	\N	call_started	\N	\N	t
884	16	7	Звонок завершён · 0:04	2026-04-24 17:20:06.083721	\N	f	\N	\N	\N	call_ended	\N	\N	t
890	5	7	\N	2026-04-25 08:59:54.455315	\N	f	\N	\N	\N	call_started	\N	\N	t
891	5	7	Звонок завершён · 0:42	2026-04-25 09:00:36.412721	\N	f	\N	\N	\N	call_ended	\N	\N	t
893	5	1	\N	2026-04-25 16:26:09.270379	\N	f	\N	\N	\N	call_started	\N	\N	t
894	5	1	Звонок завершён · 4:56	2026-04-25 16:31:05.762328	\N	f	\N	\N	\N	call_ended	\N	\N	t
936	5	1	\N	2026-04-30 12:50:25.045582	\N	f	\N	\N	\N	call_started	\N	\N	t
940	5	1	\N	2026-04-30 19:25:11.270658	\N	f	\N	\N	\N	call_started	\N	\N	t
941	5	1	Звонок завершён · 0:01	2026-04-30 19:25:12.194765	\N	f	\N	\N	\N	call_ended	\N	\N	t
958	77	1	\N	2026-05-01 15:10:34.336498	\N	f	\N	\N	\N	call_started	\N	\N	t
959	77	1	Звонок завершён · 0:09	2026-05-01 15:10:43.917558	\N	f	\N	\N	\N	call_ended	\N	\N	t
960	6	1	\N	2026-05-01 15:10:44.055817	\N	f	\N	\N	\N	call_started	\N	\N	t
961	6	1	Звонок завершён · 0:17	2026-05-01 15:11:01.097694	\N	f	\N	\N	\N	call_ended	\N	\N	t
962	77	1	\N	2026-05-01 15:11:12.329389	\N	f	\N	\N	\N	call_started	\N	\N	t
963	77	1	Звонок завершён · 0:06	2026-05-01 15:11:18.662804	\N	f	\N	\N	\N	call_ended	\N	\N	t
964	77	1	\N	2026-05-01 15:11:39.440813	\N	f	\N	\N	\N	call_started	\N	\N	t
965	77	1	\N	2026-05-01 15:13:54.094025	\N	f	\N	\N	\N	call_started	\N	\N	t
966	77	1	\N	2026-05-01 15:23:05.273619	\N	f	\N	\N	\N	call_started	\N	\N	t
968	77	1	Звонок завершён · 0:19	2026-05-01 15:23:24.361053	\N	f	\N	\N	\N	call_ended	\N	\N	t
970	77	1	\N	2026-05-01 15:40:56.023412	\N	f	\N	\N	\N	call_started	\N	\N	t
975	20	1	\N	2026-05-03 03:19:45.177575	\N	f	\N	\N	\N	call_started	\N	\N	t
1279	77	7	Здравствуйте	2026-05-12 14:05:39.937431	\N	f	\N	\N	\N	\N	\N	\N	f
977	20	1	Звонок завершён · 0:17	2026-05-03 03:20:02.98962	\N	f	\N	\N	\N	call_ended	\N	\N	t
1280	77	7	\N	2026-05-12 11:05:46.613919	\N	f	\N	\N	\N	call_started	\N	\N	t
979	6	1	\N	2026-05-03 06:02:06.71471	\N	f	\N	\N	\N	call_started	\N	\N	t
980	6	1	Звонок завершён · 0:30	2026-05-03 06:02:37.448978	\N	f	\N	\N	\N	call_ended	\N	\N	t
1281	77	7	Звонок завершён · 3:27	2026-05-12 11:09:14.579757	\N	f	\N	\N	\N	call_ended	\N	\N	t
1233	16	1	\N	2026-05-12 10:33:24.076232	2026-05-12 07:33:26.861387	t	1182	\N	\N	\N	\N	\N	f
1264	82	1	👍	2026-05-12 11:46:00.583751	\N	f	\N	\N	\N	\N	\N	\N	f
1266	81	1	\N	2026-05-12 11:46:17.497984	\N	f	\N	\N	\N	\N	\N	\N	f
1267	71	1	 	2026-05-12 11:46:48.000032	\N	f	\N	\N	\N	\N	\N	\N	f
1282	16	1	 	2026-05-12 21:05:44.37555	\N	f	\N	\N	\N	\N	2026-05-12 18:05:51.398239	1	f
1302	6	1	Протестировал	2026-05-14 07:28:09.250695	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1303	6	1	\N	2026-05-14 07:28:10.932656	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1305	16	1	Звонок завершён · 0:13	2026-05-14 07:28:37.703699	\N	f	\N	\N	\N	call_ended	\N	\N	t
1348	16	1	па	2026-05-15 16:35:11.366573	\N	f	\N	\N	\N	\N	\N	\N	f
1351	66	1	Здравствуйте	2026-05-18 13:04:34.676945	\N	f	\N	\N	\N	\N	\N	\N	f
1356	6	1	Звонок завершён · 0:05	2026-05-18 10:06:02.472227	\N	f	\N	\N	\N	call_ended	\N	\N	t
1364	6	1	\N	2026-05-25 07:43:29.592177	\N	f	\N	\N	\N	call_started	\N	\N	t
1365	6	1	Звонок завершён · 0:01	2026-05-25 07:43:31.133055	\N	f	\N	\N	\N	call_ended	\N	\N	t
1201	88	7	\N	2026-05-12 06:18:13.621459	\N	f	\N	\N	\N	chat_created	\N	\N	t
1234	5	1	\N	2026-05-12 10:33:30.714307	2026-05-12 07:33:33.423496	t	\N	1182	\N	\N	\N	\N	f
1265	68	1	🤔	2026-05-12 11:46:06.925766	\N	f	\N	\N	\N	\N	\N	\N	f
1268	71	1	\N	2026-05-12 11:48:34.922539	\N	f	\N	449	\N	\N	\N	\N	f
1283	16	1	 	2026-05-12 18:05:51.722445	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1304	16	1	\N	2026-05-14 07:28:24.250045	\N	f	\N	\N	\N	call_started	\N	\N	t
1306	74	1	\N	2026-05-14 07:28:41.060892	\N	f	\N	\N	\N	call_started	\N	\N	t
1307	74	1	Звонок завершён · 0:10	2026-05-14 07:28:51.328594	\N	f	\N	\N	\N	call_ended	\N	\N	t
1349	74	1	\N	2026-05-15 19:36:38.117974	2026-05-15 19:04:38.887893	t	\N	\N	\N	\N	\N	\N	f
1353	74	1	👍	2026-05-18 13:05:40.251965	\N	f	\N	\N	\N	\N	\N	\N	f
1355	6	1	\N	2026-05-18 10:05:57.128508	\N	f	\N	\N	\N	call_started	\N	\N	t
1352	16	1	\N	2026-05-18 13:05:29.127501	2026-05-18 16:45:36.045337	t	\N	\N	\N	\N	\N	\N	f
1202	88	7	\N	2026-05-12 06:18:13.849137	\N	f	\N	\N	1	member_added	\N	\N	t
1284	16	1	\N	2026-05-12 18:26:09.741252	\N	f	\N	\N	\N	chat_avatar_updated	\N	\N	t
1308	74	1	👍	2026-05-14 07:33:27.906294	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1350	74	1	авыа	2026-05-15 22:04:53.290413	\N	f	\N	\N	\N	\N	\N	\N	f
1354	6	1	\N	2026-05-18 10:05:50.838766	\N	f	\N	\N	\N	call_started	\N	\N	t
1235	5	1	\N	2026-05-12 10:38:13.005727	2026-05-12 08:08:08.655527	t	\N	\N	\N	\N	\N	\N	f
1269	74	1	\N	2026-05-12 12:19:09.386681	\N	f	\N	449	\N	\N	\N	\N	f
1357	77	1	 	2026-05-18 13:06:22.205224	\N	f	\N	\N	\N	\N	\N	\N	f
1285	16	1	 	2026-05-12 22:12:16.068162	\N	f	\N	\N	\N	\N	\N	\N	f
1237	5	1	\N	2026-05-12 10:38:21.203555	2026-05-12 07:38:23.283191	t	\N	\N	\N	\N	\N	\N	f
1236	5	1	\N	2026-05-12 10:38:18.024966	2026-05-12 08:08:10.401642	t	\N	\N	\N	\N	\N	\N	f
526	6	1	Апдейтнул статус	2026-03-26 09:43:41.904593	\N	f	\N	\N	\N	\N	\N	\N	f
545	6	1	@oleg посмотри, пожалуйста	2026-04-03 22:44:40.494487	\N	f	\N	\N	\N	\N	\N	\N	f
547	6	7	Уточню требования	2026-04-04 08:39:06.879918	\N	f	\N	\N	\N	\N	\N	\N	f
551	6	7	Собрал обратную связь	2026-04-04 08:39:20.500036	\N	f	\N	\N	\N	\N	\N	\N	f
556	6	1	Отложим до следующей итерации	2026-04-06 19:00:50.210213	\N	f	\N	\N	\N	\N	\N	\N	f
560	77	1	Всё ок	2026-04-07 12:59:10.781971	\N	f	\N	\N	\N	\N	2026-04-11 14:53:12.733879	1	f
561	6	7	Залил изменения	2026-04-07 12:59:19.516546	\N	f	\N	\N	\N	\N	\N	\N	f
562	6	7	Сделал	2026-04-07 12:59:30.004902	\N	f	\N	\N	\N	\N	\N	\N	f
563	6	7	Обновил ветку	2026-04-07 13:00:45.751265	\N	f	\N	\N	\N	\N	\N	\N	f
564	6	1	Тесты прошли	2026-04-07 13:00:49.238064	\N	f	\N	\N	\N	\N	\N	\N	f
565	77	7	Принято	2026-04-07 13:01:01.050021	\N	f	\N	\N	\N	\N	\N	\N	f
567	77	7	Готовлю документацию	2026-04-07 13:01:11.869375	\N	f	\N	\N	\N	\N	\N	\N	f
568	6	7	Проверяю логи	2026-04-07 13:01:15.373144	\N	f	\N	\N	\N	\N	\N	\N	f
570	77	1	Сделаю	2026-04-08 13:35:18.195641	\N	f	\N	\N	\N	\N	2026-04-11 15:06:44.022187	1	f
571	77	1	Позже скину	2026-04-08 13:35:26.830275	\N	f	\N	\N	\N	\N	2026-04-11 15:06:41.474436	1	f
572	77	1	Заметил баг	2026-04-08 13:41:43.918179	\N	f	\N	\N	\N	\N	\N	\N	f
573	77	25	Протестировал	2026-04-08 14:14:03.462086	\N	f	\N	\N	\N	\N	\N	\N	f
574	77	25	Всё работает	2026-04-08 14:14:21.624131	\N	f	\N	\N	\N	\N	\N	\N	f
575	77	1	Спасибо	2026-04-08 14:14:26.901832	\N	f	\N	\N	\N	\N	\N	\N	f
576	77	25	Поправил стили	2026-04-08 14:33:53.5774	\N	f	\N	\N	\N	\N	2026-04-11 15:06:34.885283	1	f
577	77	1	Принял задачу	2026-04-08 14:34:12.857324	\N	f	\N	\N	\N	\N	2026-04-11 14:55:27.96276	1	f
578	77	1	Начинаю	2026-04-08 14:34:17.840677	\N	f	\N	\N	\N	\N	\N	\N	f
579	77	25	Сделаю после обеда	2026-04-08 14:34:22.876081	\N	f	\N	\N	\N	\N	2026-04-11 15:06:32.328947	1	f
580	77	25	Понял, спасибо	2026-04-08 14:34:25.323628	\N	f	\N	\N	\N	\N	\N	\N	f
581	77	25	Отправил на ревью	2026-04-08 14:34:27.142579	\N	f	\N	\N	\N	\N	2026-04-11 15:06:26.469551	1	f
582	77	25	Смотрю	2026-04-08 14:34:32.614101	\N	f	\N	\N	\N	\N	\N	\N	f
583	77	25	Проверяю	2026-04-08 14:34:35.241907	\N	f	\N	\N	\N	\N	\N	\N	f
584	77	25	Обновляю	2026-04-08 14:34:36.766488	\N	f	\N	\N	\N	\N	\N	\N	f
585	77	1	В процессе	2026-04-08 14:34:41.518569	\N	f	\N	\N	\N	\N	2026-04-11 15:06:21.931262	1	f
586	81	25	Жду данные	2026-04-08 14:34:55.26858	\N	f	\N	\N	\N	\N	\N	\N	f
587	68	1	Деплою	2026-04-08 14:35:08.63084	\N	f	\N	\N	\N	\N	\N	\N	f
588	77	7	На связи	2026-04-10 09:51:55.275793	\N	f	\N	\N	\N	\N	2026-04-11 15:05:33.09502	1	f
590	6	1	Скинул ссылку	2026-04-10 09:52:12.37457	\N	f	\N	\N	\N	\N	\N	\N	f
591	6	1	Ознакомился	2026-04-10 09:52:15.792135	\N	f	\N	\N	\N	\N	\N	\N	f
592	6	1	Есть замечания	2026-04-10 09:52:18.398657	\N	f	\N	\N	\N	\N	\N	\N	f
593	6	1	Сделал рефакторинг	2026-04-10 09:52:20.245946	\N	f	\N	\N	\N	\N	\N	\N	f
594	6	1	Обсудим завтра	2026-04-10 09:56:33.306139	\N	f	\N	\N	\N	\N	\N	\N	f
597	6	1	Можно править	2026-04-10 09:56:59.614473	\N	f	\N	\N	\N	\N	\N	\N	f
600	6	7	Ок	2026-04-10 09:57:34.918801	\N	f	\N	\N	\N	\N	\N	\N	f
601	6	7	Проверил логи	2026-04-10 09:57:38.538886	\N	f	\N	\N	\N	\N	\N	\N	f
602	77	7	Сделал	2026-04-10 09:57:44.935718	\N	f	\N	\N	\N	\N	\N	\N	f
603	6	1	Готово к тестированию	2026-04-10 09:57:51.005904	\N	f	\N	\N	\N	\N	\N	\N	f
605	6	7	Проверьте стейджинг	2026-04-10 09:58:13.039072	\N	f	\N	\N	\N	\N	\N	\N	f
606	6	7	Всё ок	2026-04-10 09:58:15.037792	\N	f	\N	\N	\N	\N	\N	\N	f
607	6	7	Собрал билд	2026-04-10 09:58:16.3959	\N	f	\N	\N	\N	\N	\N	\N	f
608	6	7	Отлично	2026-04-10 09:58:18.055448	\N	f	\N	\N	\N	\N	\N	\N	f
610	6	7	Проверяю фиксы	2026-04-10 09:59:57.142667	\N	f	\N	\N	\N	\N	\N	\N	f
611	6	7	Мёржим	2026-04-10 10:00:04.254383	\N	f	\N	\N	\N	\N	\N	\N	f
612	6	7	Готово	2026-04-10 10:00:06.79363	\N	f	\N	\N	\N	\N	\N	\N	f
613	6	7	Сделал ревью	2026-04-10 10:00:13.409807	\N	f	\N	\N	\N	\N	\N	\N	f
614	6	7	Жду мёрж	2026-04-10 10:00:15.977961	\N	f	\N	\N	\N	\N	\N	\N	f
615	6	7	Ок, вижу	2026-04-10 10:00:22.565478	\N	f	\N	\N	\N	\N	\N	\N	f
616	6	7	Проверяю бранч	2026-04-10 10:04:16.422599	\N	f	\N	\N	\N	\N	\N	\N	f
617	6	7	Нашёл ошибку	2026-04-10 10:04:19.404205	\N	f	\N	\N	\N	\N	\N	\N	f
618	6	7	Исправил	2026-04-10 10:04:21.115335	\N	f	\N	\N	\N	\N	\N	\N	f
619	6	7	Протестирую позже	2026-04-10 10:04:23.340476	\N	f	\N	\N	\N	\N	\N	\N	f
620	6	7	Подтверждаю	2026-04-10 10:06:51.962793	\N	f	\N	\N	\N	\N	2026-04-11 12:14:57.37371	1	f
621	6	7	Отправил	2026-04-10 10:06:58.405453	\N	f	\N	\N	\N	\N	\N	\N	f
622	77	1	На сегодня всё	2026-04-10 10:08:31.578349	\N	f	\N	\N	\N	\N	2026-04-11 15:05:16.698377	1	f
623	6	7	Принято, спасибо	2026-04-10 10:08:36.17279	\N	f	\N	\N	\N	\N	\N	\N	f
624	6	7	Понял задачу	2026-04-10 10:08:38.145798	\N	f	\N	\N	\N	\N	\N	\N	f
626	77	1	Спрошу у команды	2026-04-10 10:08:42.277364	\N	f	\N	\N	\N	\N	2026-04-11 15:06:06.081741	7	f
627	77	1	Да, хорошо	2026-04-10 10:08:44.491589	\N	f	\N	\N	\N	\N	2026-04-11 15:05:21.733203	1	f
628	77	1	Ок, договорились	2026-04-10 10:08:46.572137	\N	f	\N	\N	\N	\N	2026-04-11 14:55:39.314346	1	f
1193	86	7	Проверьте, пожалуйста	2026-05-12 09:07:53.207285	\N	f	\N	\N	\N	\N	\N	\N	f
1203	88	7	Посмотрите	2026-05-12 09:18:17.099268	\N	f	\N	\N	\N	\N	\N	\N	f
625	6	7	Уточню	2026-04-10 10:08:39.605851	\N	f	\N	\N	\N	\N	2026-05-13 09:24:19.711498	1	f
559	6	1	На проверке	2026-04-07 12:59:07.739816	\N	f	\N	\N	\N	\N	2026-05-13 09:24:29.777165	1	f
604	6	1	Задеплоил	2026-04-10 09:58:07.315807	\N	f	\N	\N	\N	\N	\N	\N	f
1309	16	1	\N	2026-05-14 07:33:46.008994	\N	f	\N	\N	\N	call_started	\N	\N	t
595	6	1	Принял	2026-04-10 09:56:40.197515	\N	f	\N	\N	\N	\N	2026-05-14 11:30:58.29637	7	f
566	6	1	\N	2026-04-07 13:01:05.425274	2026-05-16 12:40:07.720645	t	\N	\N	\N	\N	\N	\N	f
1358	77	1	kjkj	2026-05-18 19:44:29.782142	\N	f	\N	\N	\N	\N	\N	\N	f
1361	16	1	\N	2026-05-18 19:44:54.47673	2026-05-18 16:45:26.552486	t	\N	\N	\N	\N	\N	\N	f
1286	16	1	\N	2026-05-12 19:24:05.069097	\N	f	\N	\N	\N	chat_avatar_updated	\N	\N	t
1310	16	1	Звонок завершён · 0:40	2026-05-14 07:34:26.66273	\N	f	\N	\N	\N	call_ended	\N	\N	t
1359	16	1	kll	2026-05-18 19:44:41.961448	\N	f	\N	\N	\N	\N	\N	\N	f
1238	5	1	\N	2026-05-12 10:47:53.495712	2026-05-12 07:47:55.971147	t	\N	\N	\N	\N	\N	\N	f
1239	5	1	\N	2026-05-12 10:48:07.527727	2026-05-12 07:48:09.750875	t	\N	\N	\N	\N	\N	\N	f
1362	16	1	\N	2026-05-18 19:45:02.967841	2026-05-18 16:45:27.683485	t	\N	\N	\N	\N	\N	\N	f
1240	16	1	\N	2026-05-12 10:48:52.819846	2026-05-12 07:48:56.626173	t	\N	\N	\N	\N	\N	\N	f
1287	6	1	Уточню	2026-05-13 09:24:20.012899	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1291	16	1	\N	2026-05-13 09:24:51.204466	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1241	16	1	\N	2026-05-12 10:54:39.672046	2026-05-12 07:54:44.007862	t	\N	\N	\N	\N	\N	\N	f
1292	16	1	😀	2026-05-13 09:24:52.506598	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1293	16	1	Можете пожалуйста скинуть задание	2026-05-13 09:24:55.076243	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1311	28	7	авы	2026-05-14 14:29:16.741407	\N	f	\N	\N	\N	\N	\N	\N	f
1315	28	7	авы	2026-05-14 14:29:23.278742	\N	f	\N	\N	\N	\N	\N	\N	f
1319	28	7	вы	2026-05-14 14:29:24.396369	\N	f	\N	\N	\N	\N	\N	\N	f
1323	28	7	ыа	2026-05-14 14:29:25.200176	\N	f	\N	\N	\N	\N	\N	\N	f
1327	28	7	аы	2026-05-14 14:29:25.997809	\N	f	\N	\N	\N	\N	\N	\N	f
1331	28	7	авы	2026-05-14 14:29:39.808046	\N	f	\N	\N	\N	\N	\N	\N	f
1335	28	7	авы	2026-05-14 14:29:44.363165	\N	f	\N	\N	\N	\N	\N	\N	f
1360	16	1	lk	2026-05-18 19:44:51.005347	\N	f	\N	\N	\N	\N	\N	\N	f
1363	16	1	\N	2026-05-18 19:45:09.988564	2026-05-18 16:45:29.28843	t	\N	\N	\N	\N	\N	\N	f
1288	6	1	На проверке	2026-05-13 09:24:29.788315	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1312	28	7	авы	2026-05-14 14:29:18.31653	\N	f	\N	\N	\N	\N	\N	\N	f
1242	5	1	\N	2026-05-12 10:54:48.327862	2026-05-12 07:54:50.954239	t	\N	\N	\N	\N	\N	\N	f
1316	28	7	авы	2026-05-14 14:29:23.65261	\N	f	\N	\N	\N	\N	\N	\N	f
1320	28	7	выа	2026-05-14 14:29:24.598851	\N	f	\N	\N	\N	\N	\N	\N	f
1244	5	1	\N	2026-05-12 10:55:19.901274	2026-05-12 07:57:46.278654	t	\N	\N	\N	\N	\N	\N	f
1243	5	1	\N	2026-05-12 10:55:16.394019	2026-05-12 07:57:48.296031	t	\N	\N	\N	\N	\N	\N	f
1324	28	7	вва	2026-05-14 14:29:25.385827	\N	f	\N	\N	\N	\N	\N	\N	f
1328	28	7	авы	2026-05-14 14:29:26.172999	\N	f	\N	\N	\N	\N	\N	\N	f
1332	28	7	авы	2026-05-14 14:29:41.899416	\N	f	\N	\N	\N	\N	\N	\N	f
1336	28	7	авы	2026-05-14 14:29:45.160677	\N	f	\N	\N	\N	\N	\N	\N	f
1019	77	1	\N	2026-05-09 14:44:03.84076	2026-05-12 10:46:58.417841	t	\N	\N	\N	\N	\N	\N	f
995	6	1	\N	2026-05-04 20:55:21.617335	2026-05-12 10:48:16.89564	t	\N	\N	\N	\N	\N	\N	f
989	6	1	\N	2026-05-04 20:52:14.571082	\N	f	\N	\N	\N	\N	\N	\N	f
990	6	1	\N	2026-05-04 20:52:36.655779	\N	f	\N	\N	\N	\N	\N	\N	f
991	6	1	\N	2026-05-04 20:52:52.416775	\N	f	\N	\N	\N	\N	\N	\N	f
992	6	1	\N	2026-05-04 20:53:10.163814	\N	f	\N	\N	\N	\N	\N	\N	f
993	6	1	\N	2026-05-04 17:54:26.532652	\N	f	\N	\N	\N	call_started	\N	\N	t
994	6	1	\N	2026-05-04 20:54:39.689646	\N	f	\N	\N	\N	\N	\N	\N	f
1245	16	1	\N	2026-05-12 11:02:59.693419	2026-05-12 08:03:04.394035	t	\N	\N	\N	\N	\N	\N	f
1049	16	1	\N	2026-05-11 06:45:01.994895	2026-05-12 08:15:10.059066	t	\N	\N	\N	\N	\N	\N	f
952	16	7	Принято	2026-04-30 22:45:41.689678	\N	f	\N	\N	\N	\N	\N	\N	f
1000	6	1	Звонок завершён · 2:11	2026-05-04 17:56:37.595515	\N	f	\N	\N	\N	call_ended	\N	\N	t
1002	6	1	\N	2026-05-04 21:16:40.144781	\N	f	\N	\N	\N	\N	\N	\N	f
1003	6	1	\N	2026-05-04 21:22:47.920823	\N	f	\N	\N	\N	\N	\N	\N	f
1004	6	1	\N	2026-05-04 21:37:15.251138	\N	f	\N	\N	\N	\N	\N	\N	f
1021	77	1	\N	2026-05-09 14:55:05.998515	2026-05-12 11:00:31.427567	t	\N	\N	\N	\N	\N	\N	f
1289	16	1	\N	2026-05-13 09:24:38.447772	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1290	16	1	Здравствуйте	2026-05-13 09:24:40.286733	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1294	6	1	Перенёс на завтра	2026-05-13 09:25:05.520105	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1295	6	1	\N	2026-05-13 09:25:08.673815	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1296	6	1	Перенёс на завтра	2026-05-13 09:25:13.113827	\N	f	\N	\N	\N	message_pinned	\N	\N	t
987	6	1	Протестировал	2026-05-04 20:49:52.182636	\N	f	\N	\N	\N	\N	\N	\N	f
1313	28	7	авы	2026-05-14 14:29:22.302298	\N	f	\N	\N	\N	\N	\N	\N	f
1317	28	7	авы	2026-05-14 14:29:23.952656	\N	f	\N	\N	\N	\N	\N	\N	f
487	5	1	\N	2026-03-06 18:58:57.768754	2026-05-07 16:41:56.83007	t	\N	\N	23	\N	\N	\N	f
1001	6	1	\N	2026-05-04 21:16:21.858955	2026-05-07 16:54:04.843268	t	\N	\N	\N	\N	\N	\N	f
967	6	1	\N	2026-05-01 18:23:20.709562	2026-05-07 16:54:08.142179	t	\N	\N	\N	\N	\N	\N	f
596	6	1	\N	2026-04-10 09:56:42.163596	2026-05-07 16:54:12.555954	t	\N	\N	\N	\N	\N	\N	f
1015	6	1	\N	2026-05-08 15:53:16.817993	\N	f	\N	\N	10	member_added	\N	\N	t
1016	6	1	\N	2026-05-08 15:53:16.988123	\N	f	\N	\N	7	role_changed	\N	\N	t
1321	28	7	ыа	2026-05-14 14:29:24.794002	\N	f	\N	\N	\N	\N	\N	\N	f
1325	28	7	а	2026-05-14 14:29:25.597759	\N	f	\N	\N	\N	\N	\N	\N	f
1329	28	7	аыаы	2026-05-14 14:29:27.422583	\N	f	\N	\N	\N	\N	\N	\N	f
1333	28	7	авы	2026-05-14 14:29:42.704134	\N	f	\N	\N	\N	\N	\N	\N	f
1337	28	7	авы	2026-05-14 14:29:45.905347	\N	f	\N	\N	\N	\N	\N	\N	f
1026	6	7	\N	2026-05-09 12:53:46.443419	\N	f	\N	\N	\N	call_started	\N	\N	t
1027	6	7	Звонок завершён · 0:16	2026-05-09 12:54:02.901057	\N	f	\N	\N	\N	call_ended	\N	\N	t
1028	77	7	\N	2026-05-09 13:42:02.11842	\N	f	\N	\N	\N	call_started	\N	\N	t
1029	77	7	Звонок завершён · 2:08	2026-05-09 13:44:10.548928	\N	f	\N	\N	\N	call_ended	\N	\N	t
1030	16	1	\N	2026-05-09 13:44:10.720038	\N	f	\N	\N	\N	call_started	\N	\N	t
1031	16	1	Звонок завершён · 1:22:03	2026-05-09 15:06:14.440068	\N	f	\N	\N	\N	call_ended	\N	\N	t
1032	16	1	\N	2026-05-10 10:55:32.957294	\N	f	\N	\N	\N	\N	\N	\N	f
1035	16	1	\N	2026-05-10 18:50:57.266245	\N	f	\N	\N	\N	call_started	\N	\N	t
1036	16	1	Звонок завершён · 1:28	2026-05-10 18:52:25.457849	\N	f	\N	\N	\N	call_ended	\N	\N	t
447	16	1	Опрос	2026-03-01 12:34:00.690955	\N	f	\N	\N	\N	\N	2026-05-10 19:15:20.370197	1	f
1037	16	1	\N	2026-05-10 19:50:43.511904	\N	f	\N	\N	\N	call_started	\N	\N	t
1038	16	1	Звонок завершён · 0:06	2026-05-10 19:50:50.340042	\N	f	\N	\N	\N	call_ended	\N	\N	t
450	16	7	Здравствуйте	2026-03-01 12:36:19.673971	\N	f	\N	\N	\N	\N	2026-05-11 03:12:18.667433	1	f
1033	16	1	\N	2026-05-10 10:55:48.892394	\N	f	\N	\N	\N	\N	2026-05-11 03:12:33.122653	1	f
1039	16	1	\N	2026-05-11 03:12:49.086819	\N	f	\N	\N	\N	call_started	\N	\N	t
1040	16	1	Звонок завершён · 0:40	2026-05-11 03:13:29.443006	\N	f	\N	\N	\N	call_ended	\N	\N	t
1023	16	1	\N	2026-05-09 14:55:37.529769	\N	f	\N	\N	\N	\N	2026-05-11 03:28:48.232773	1	f
1041	16	1	\N	2026-05-11 03:28:48.552857	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1024	16	1	\N	2026-05-09 14:55:55.640704	\N	f	\N	\N	\N	\N	2026-05-11 03:28:54.276458	1	f
1042	16	1	\N	2026-05-11 03:28:54.306077	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1043	16	1	\N	2026-05-11 03:29:10.918173	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1044	16	1		2026-05-11 03:29:23.113631	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1034	16	1	\N	2026-05-10 10:57:32.997466	\N	f	\N	\N	\N	\N	\N	\N	f
1045	16	1	\N	2026-05-11 03:29:33.082819	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1052	16	1	Здравствуйте	2026-05-11 03:50:06.091976	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1053	5	1	\N	2026-05-11 05:27:49.240627	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1054	5	1	fdsfsd	2026-05-11 05:27:58.735577	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1213	90	7	\N	2026-05-12 06:31:51.014812	\N	f	\N	\N	\N	chat_created	\N	\N	t
1214	90	7	\N	2026-05-12 06:31:51.244115	\N	f	\N	\N	1	member_added	\N	\N	t
1297	6	1	Задеплоил	2026-05-13 09:46:08.294338	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1298	6	1	\N	2026-05-13 09:46:11.127466	\N	f	\N	\N	\N	message_unpinned	\N	\N	t
1314	28	7	авы	2026-05-14 14:29:22.864702	\N	f	\N	\N	\N	\N	\N	\N	f
1318	28	7	авы	2026-05-14 14:29:24.193645	\N	f	\N	\N	\N	\N	\N	\N	f
1322	28	7	ыав	2026-05-14 14:29:24.998176	\N	f	\N	\N	\N	\N	\N	\N	f
1326	28	7	вы	2026-05-14 14:29:25.783625	\N	f	\N	\N	\N	\N	\N	\N	f
1246	16	1	\N	2026-05-12 11:08:22.503008	2026-05-12 08:08:27.241636	t	\N	\N	\N	\N	\N	\N	f
1219	90	1	Жду ответа	2026-05-12 09:32:08.309293	\N	f	\N	\N	\N	\N	\N	\N	f
1330	28	7	а	2026-05-14 14:29:36.907651	\N	f	\N	\N	\N	\N	\N	\N	f
1334	28	7	авы	2026-05-14 14:29:43.548719	\N	f	\N	\N	\N	\N	\N	\N	f
1098	5	7	\N	2026-05-11 19:10:05.846775	\N	f	\N	\N	\N	call_started	\N	\N	t
1099	5	7	Звонок завершён · 1:15	2026-05-11 19:11:21.381537	\N	f	\N	\N	\N	call_ended	\N	\N	t
1100	5	1	\N	2026-05-11 19:16:58.30015	\N	f	\N	\N	\N	call_started	\N	\N	t
1101	5	1	Звонок завершён · 0:18	2026-05-11 19:17:17.103793	\N	f	\N	\N	\N	call_ended	\N	\N	t
1102	5	1	\N	2026-05-11 19:17:50.552138	\N	f	\N	\N	\N	call_started	\N	\N	t
1103	5	1	Звонок завершён · 0:11	2026-05-11 19:18:01.606458	\N	f	\N	\N	\N	call_ended	\N	\N	t
1104	5	7	\N	2026-05-11 19:18:37.708736	\N	f	\N	\N	\N	call_started	\N	\N	t
1105	5	7	Звонок завершён · 1:03	2026-05-11 19:19:40.799233	\N	f	\N	\N	\N	call_ended	\N	\N	t
1106	5	7	\N	2026-05-11 19:25:32.214646	\N	f	\N	\N	\N	call_started	\N	\N	t
1107	5	7	Звонок завершён · 3:31	2026-05-11 19:29:03.733641	\N	f	\N	\N	\N	call_ended	\N	\N	t
1108	5	7	\N	2026-05-11 19:30:36.215811	\N	f	\N	\N	\N	call_started	\N	\N	t
1109	5	7	Звонок завершён · 2:22	2026-05-11 19:32:58.135864	\N	f	\N	\N	\N	call_ended	\N	\N	t
1110	5	1	\N	2026-05-11 19:33:09.903615	\N	f	\N	\N	\N	call_started	\N	\N	t
1111	5	1	Звонок завершён · 0:32	2026-05-11 19:33:42.06377	\N	f	\N	\N	\N	call_ended	\N	\N	t
1114	5	7	\N	2026-05-11 19:38:51.488029	\N	f	\N	\N	\N	call_started	\N	\N	t
1115	5	7	Звонок завершён · 3:52	2026-05-11 19:42:43.989552	\N	f	\N	\N	\N	call_ended	\N	\N	t
1116	5	1	\N	2026-05-11 19:43:36.069114	\N	f	\N	\N	\N	call_started	\N	\N	t
1117	5	1	Звонок завершён · 0:15	2026-05-11 19:43:51.164727	\N	f	\N	\N	\N	call_ended	\N	\N	t
1118	5	1	\N	2026-05-11 19:47:47.18027	\N	f	\N	\N	\N	call_started	\N	\N	t
1119	5	1	Звонок завершён · 0:12	2026-05-11 19:47:59.433103	\N	f	\N	\N	\N	call_ended	\N	\N	t
1120	5	7	\N	2026-05-11 19:48:02.42786	\N	f	\N	\N	\N	call_started	\N	\N	t
1121	5	7	Звонок завершён · 0:14	2026-05-11 19:48:17.038193	\N	f	\N	\N	\N	call_ended	\N	\N	t
1122	5	1	\N	2026-05-11 19:48:19.101839	\N	f	\N	\N	\N	call_started	\N	\N	t
1123	5	1	Звонок завершён · 0:22	2026-05-11 19:48:41.142925	\N	f	\N	\N	\N	call_ended	\N	\N	t
1097	5	1	Опрос	2026-05-11 16:50:43.123743	\N	f	\N	447	\N	\N	2026-05-11 19:48:45.662882	1	f
1124	5	1	Опрос	2026-05-11 19:48:46.12413	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1125	5	1	\N	2026-05-11 19:50:15.855991	\N	f	\N	\N	\N	chat_avatar_updated	\N	\N	t
1126	5	7	\N	2026-05-11 21:49:53.078028	\N	f	\N	\N	\N	call_started	\N	\N	t
1127	5	7	Звонок завершён · 0:33	2026-05-11 21:50:26.434288	\N	f	\N	\N	\N	call_ended	\N	\N	t
1128	5	1	\N	2026-05-11 21:50:29.039685	\N	f	\N	\N	\N	call_started	\N	\N	t
1129	5	1	Звонок завершён · 2:16	2026-05-11 21:52:45.350382	\N	f	\N	\N	\N	call_ended	\N	\N	t
1130	5	1	\N	2026-05-11 21:57:19.787213	\N	f	\N	\N	\N	call_started	\N	\N	t
1131	5	1	Звонок завершён · 4:03	2026-05-11 22:01:22.798096	\N	f	\N	\N	\N	call_ended	\N	\N	t
1132	5	7	\N	2026-05-11 22:31:51.268945	\N	f	\N	\N	\N	call_started	\N	\N	t
1133	5	7	Звонок завершён · 0:36	2026-05-11 22:32:27.562561	\N	f	\N	\N	\N	call_ended	\N	\N	t
1134	5	1	\N	2026-05-11 22:32:30.80377	\N	f	\N	\N	\N	call_started	\N	\N	t
1135	5	1	Звонок завершён · 1:28	2026-05-11 22:33:58.934757	\N	f	\N	\N	\N	call_ended	\N	\N	t
1136	5	7	\N	2026-05-11 22:34:02.32897	\N	f	\N	\N	\N	call_started	\N	\N	t
1137	5	7	Звонок завершён · 2:58	2026-05-11 22:37:01.310625	\N	f	\N	\N	\N	call_ended	\N	\N	t
1138	5	1	\N	2026-05-11 22:38:43.269792	\N	f	\N	\N	\N	call_started	\N	\N	t
1139	5	1	Звонок завершён · 0:09	2026-05-11 22:38:52.623657	\N	f	\N	\N	\N	call_ended	\N	\N	t
1140	5	7	\N	2026-05-11 22:38:53.855101	\N	f	\N	\N	\N	call_started	\N	\N	t
1141	5	7	Звонок завершён · 2:00	2026-05-11 22:40:54.726433	\N	f	\N	\N	\N	call_ended	\N	\N	t
1142	5	1	\N	2026-05-11 22:40:58.153238	\N	f	\N	\N	\N	call_started	\N	\N	t
1143	5	1	\N	2026-05-11 22:44:34.794822	\N	f	\N	\N	\N	call_started	\N	\N	t
1144	5	1	\N	2026-05-11 22:58:16.249504	\N	f	\N	\N	\N	call_started	\N	\N	t
1145	5	1	Звонок завершён · 0:36	2026-05-11 22:58:53.062037	\N	f	\N	\N	\N	call_ended	\N	\N	t
1146	5	7	\N	2026-05-11 22:58:54.342763	\N	f	\N	\N	\N	call_started	\N	\N	t
1147	5	7	Звонок завершён · 1:28	2026-05-11 23:00:23.096396	\N	f	\N	\N	\N	call_ended	\N	\N	t
1148	5	7	\N	2026-05-11 23:00:24.224006	\N	f	\N	\N	\N	call_started	\N	\N	t
1149	5	7	Звонок завершён · 4:13	2026-05-11 23:04:37.828288	\N	f	\N	\N	\N	call_ended	\N	\N	t
1150	5	1	\N	2026-05-11 23:06:39.113742	\N	f	\N	\N	\N	call_started	\N	\N	t
1151	5	1	Звонок завершён · 0:13	2026-05-11 23:06:52.423738	\N	f	\N	\N	\N	call_ended	\N	\N	t
1152	5	7	\N	2026-05-11 23:06:53.743722	\N	f	\N	\N	\N	call_started	\N	\N	t
1153	5	7	Звонок завершён · 0:42	2026-05-11 23:07:36.638126	\N	f	\N	\N	\N	call_ended	\N	\N	t
1154	5	1	\N	2026-05-11 23:08:24.394449	\N	f	\N	\N	\N	call_started	\N	\N	t
1155	5	1	Звонок завершён · 3:42	2026-05-11 23:12:06.865447	\N	f	\N	\N	\N	call_ended	\N	\N	t
1156	5	1	\N	2026-05-11 23:12:09.478016	\N	f	\N	\N	\N	call_started	\N	\N	t
1157	5	1	Звонок завершён · 0:08	2026-05-11 23:12:17.602076	\N	f	\N	\N	\N	call_ended	\N	\N	t
1158	5	7	\N	2026-05-11 23:12:19.313932	\N	f	\N	\N	\N	call_started	\N	\N	t
1159	5	7	Звонок завершён · 2:03	2026-05-11 23:14:22.966801	\N	f	\N	\N	\N	call_ended	\N	\N	t
1160	5	7	\N	2026-05-11 23:16:13.548665	\N	f	\N	\N	\N	call_started	\N	\N	t
1161	5	7	Звонок завершён · 0:11	2026-05-11 23:16:25.519329	\N	f	\N	\N	\N	call_ended	\N	\N	t
1162	5	1	\N	2026-05-11 23:16:26.492818	\N	f	\N	\N	\N	call_started	\N	\N	t
1247	16	1	\N	2026-05-12 11:08:50.744186	2026-05-12 08:08:53.8487	t	\N	\N	\N	\N	\N	\N	f
1215	90	7	Готово	2026-05-12 09:31:54.46419	\N	f	\N	\N	\N	\N	\N	\N	f
1218	90	7	Ознакомился	2026-05-12 09:32:03.790294	\N	f	\N	\N	\N	\N	\N	\N	f
1299	6	1	\N	2026-05-13 12:56:49.37562	2026-05-14 07:51:18.564477	t	\N	\N	\N	\N	\N	\N	f
1338	6	7	Принял	2026-05-14 11:30:58.695689	\N	f	\N	\N	\N	message_pinned	\N	\N	t
1163	5	1	Звонок завершён · 0:07	2026-05-11 23:16:34.295293	\N	f	\N	\N	\N	call_ended	\N	\N	t
1164	5	1	\N	2026-05-11 23:18:38.196641	\N	f	\N	\N	\N	call_started	\N	\N	t
1165	5	1	Звонок завершён · 0:21	2026-05-11 23:18:59.385273	\N	f	\N	\N	\N	call_ended	\N	\N	t
1166	5	1	\N	2026-05-11 23:19:00.487928	\N	f	\N	\N	\N	call_started	\N	\N	t
1167	5	1	Звонок завершён · 0:12	2026-05-11 23:19:12.772103	\N	f	\N	\N	\N	call_ended	\N	\N	t
1168	5	7	\N	2026-05-11 23:19:13.967244	\N	f	\N	\N	\N	call_started	\N	\N	t
1169	5	7	Звонок завершён · 0:11	2026-05-11 23:19:25.244224	\N	f	\N	\N	\N	call_ended	\N	\N	t
1170	5	7	\N	2026-05-11 23:19:26.624337	\N	f	\N	\N	\N	call_started	\N	\N	t
1171	5	7	Звонок завершён · 0:07	2026-05-11 23:19:33.630062	\N	f	\N	\N	\N	call_ended	\N	\N	t
1172	5	1	\N	2026-05-11 23:19:35.062201	\N	f	\N	\N	\N	call_started	\N	\N	t
1173	5	1	Звонок завершён · 1:11	2026-05-11 23:20:46.743313	\N	f	\N	\N	\N	call_ended	\N	\N	t
1174	5	7	\N	2026-05-11 23:21:25.9649	\N	f	\N	\N	\N	call_started	\N	\N	t
1175	5	7	Звонок завершён · 1:05	2026-05-11 23:22:31.3249	\N	f	\N	\N	\N	call_ended	\N	\N	t
1176	5	1	\N	2026-05-11 23:22:34.043111	\N	f	\N	\N	\N	call_started	\N	\N	t
1177	5	1	Звонок завершён · 0:05	2026-05-11 23:22:39.354512	\N	f	\N	\N	\N	call_ended	\N	\N	t
1178	5	7	\N	2026-05-11 23:26:49.185076	\N	f	\N	\N	\N	call_started	\N	\N	t
1179	5	7	Звонок завершён · 0:24	2026-05-11 23:27:13.38607	\N	f	\N	\N	\N	call_ended	\N	\N	t
1180	5	7	\N	2026-05-11 23:27:14.535151	\N	f	\N	\N	\N	call_started	\N	\N	t
1181	5	7	Звонок завершён · 0:05	2026-05-11 23:27:19.829657	\N	f	\N	\N	\N	call_ended	\N	\N	t
1182	16	1	\N	2026-05-12 03:14:15.07899	\N	f	\N	\N	\N	\N	\N	\N	f
1184	5	1	\N	2026-05-12 02:33:04.393034	\N	f	\N	\N	7	member_removed	\N	\N	t
1185	16	1	\N	2026-05-12 05:11:50.305215	\N	f	\N	\N	\N	call_started	\N	\N	t
1186	16	1	Звонок завершён · 0:04	2026-05-12 05:11:54.544579	\N	f	\N	\N	\N	call_ended	\N	\N	t
1014	6	1	\N	2026-05-07 19:39:58.226515	\N	f	\N	\N	\N	\N	\N	\N	f
1013	6	1	\N	2026-05-07 19:39:21.241478	2026-05-12 10:47:03.717358	t	\N	\N	\N	\N	\N	\N	f
1047	16	1	\N	2026-05-11 06:42:11.295774	2026-05-11 03:42:19.93826	t	\N	\N	\N	\N	\N	\N	f
1048	16	1	Здравствуйте	2026-05-11 06:44:43.101256	\N	f	\N	\N	\N	\N	2026-05-13 09:24:40.274456	1	f
1277	77	1	\N	2026-05-12 14:04:41.467287	2026-05-13 09:57:03.507877	t	\N	\N	\N	\N	\N	\N	f
1300	6	1	\N	2026-05-13 09:57:42.856664	\N	f	\N	\N	\N	call_started	\N	\N	t
1301	6	1	Звонок завершён · 0:02	2026-05-13 09:57:44.932967	\N	f	\N	\N	\N	call_ended	\N	\N	t
1339	2	7	А кто	2026-05-14 14:36:47.704532	\N	f	\N	\N	\N	\N	\N	\N	f
1183	16	1	\N	2026-05-12 05:15:49.224534	2026-05-12 08:02:26.074974	t	\N	\N	\N	\N	\N	\N	f
1248	5	1	\N	2026-05-12 11:09:24.98703	2026-05-12 08:14:51.425522	t	\N	\N	\N	\N	\N	\N	f
1050	16	1	\N	2026-05-11 06:45:49.887566	2026-05-12 08:15:08.822336	t	\N	\N	\N	\N	\N	\N	f
889	5	7	Ок	2026-04-25 11:49:12.843279	\N	f	\N	\N	\N	\N	\N	\N	f
1012	6	1	Проверил задачу	2026-05-07 19:39:09.130912	\N	f	\N	\N	\N	\N	\N	\N	f
1216	90	1	Сделал	2026-05-12 09:31:59.141315	\N	f	\N	\N	\N	\N	\N	\N	f
\.


--
-- Data for Name: poll_options; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.poll_options (id, poll_id, option_text, "position") FROM stdin;
22	9	Каждый час	0
23	9	Несколько раз в день	1
24	9	Раз в день	2
25	9	Редко	3
46	20	Тестовый вариант 1	0
47	20	Тестовый вариант 2	1
48	21	lklkkl	0
49	21	hgfhf	1
50	21	hgfhh	2
71	29	авы	0
72	29	аыв	1
73	29	авы	2
74	30	Тестовый вариант 1	0
75	30	Тестовый вариант 2	1
76	30	Тестовый вариант 3	2
77	31	Вариант 1	0
78	31	Вариант 2	1
79	31	Вариант 3	2
80	32	я	0
81	32	ты	1
82	32	вы	2
83	33	123	0
84	33	312	1
85	33	321	2
86	33	2212	3
87	33	321	4
88	33	321	5
89	33	321	6
90	33	321	7
91	33	321	8
92	33	321	9
\.


--
-- Data for Name: poll_votes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.poll_votes (id, poll_id, option_id, user_id, voted_at) FROM stdin;
211	29	72	1	2026-05-10 10:54:51.90602
212	20	46	1	2026-05-12 11:15:23.193006
215	30	76	1	2026-05-12 11:26:43.313611
216	30	74	7	2026-05-12 11:27:00.554916
217	31	77	1	2026-05-12 13:52:49.921929
218	31	78	1	2026-05-12 13:52:49.921929
219	32	80	7	2026-05-14 14:36:54.640002
220	33	84	1	2026-05-15 09:03:43.925447
221	33	85	1	2026-05-15 09:03:43.925447
222	33	86	1	2026-05-15 09:03:43.925447
94	20	47	7	2026-03-01 12:36:06.329567
124	21	49	7	2026-04-07 12:58:42.125252
141	21	50	1	2026-04-16 16:14:16.321672
158	9	22	7	2026-04-25 17:24:15.387345
159	9	23	7	2026-04-25 17:24:15.387345
\.


--
-- Data for Name: polls; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.polls (id, message_id, is_anonymous, allows_multiple_answers, closes_at) FROM stdin;
9	238	f	t	\N
20	447	t	f	\N
21	556	t	t	\N
29	1021	t	f	2026-05-10 07:55:12.386202
30	1262	f	f	\N
31	1271	t	t	\N
32	1339	t	f	\N
33	1346	f	t	\N
\.


--
-- Data for Name: refresh_tokens; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.refresh_tokens (id, user_id, token_hash, jwt_id, created_at, expires_at, used_at, revoked_at, replaced_by_token_id, family_id) FROM stdin;
35	1	dDoZiskh2HJb3ZOdyAOP+F0wPqHdlQG1fBDQgLXgH2I=	62bf947c-84aa-4aa0-ba08-8c93f0fb514f	2026-03-09 08:57:47.578094	2026-04-08 08:57:47.57813	\N	2026-03-11 08:14:23.056586	\N	ebe45d87-add2-4aab-a5a6-cd4e7e38b008
450	1	kufAkwwVf10Z9eBzQ08HQbgB/UU+9BWw9rPOaEeN7UU=	986f4713-3e72-404c-8ed3-9996ac5590d7	2026-04-30 14:45:21.877423	2026-05-30 14:45:21.877501	2026-04-30 15:10:10.81605	\N	451	1ec4f6f6-b212-471a-9704-f8689e11e9b7
37	1	HXMZA8eU4N5LQ293VoqywyEWXJgIZJhmLXwS9AOp6Ms=	9477abab-25f2-4ada-9ed9-954693b790d9	2026-03-09 10:15:47.720763	2026-04-08 10:15:47.720822	\N	2026-03-11 08:14:23.056586	\N	3fcd1356-e312-4ac8-8c8e-4472330d8ec0
348	1	IuO7Fg1PBZBEzcOgxd8n4fLs7Z5wxcYnvvJaoz+QWPY=	05ef985a-98ae-47eb-88ad-ed1f028911b5	2026-04-23 12:51:22.914392	2026-05-23 12:51:22.914603	2026-04-23 13:16:55.778714	\N	349	68efd69b-5995-4fbb-83ed-b8706dacb092
40	1	J6Ja3/Suu7N+WoC3DlFrT9fPQKfCL2rwNH+JlKqjMl0=	912d517c-3394-4cfa-8dfa-1eeb4372d6ef	2026-03-09 16:30:21.929492	2026-04-08 16:30:21.929521	\N	2026-03-11 08:14:23.056586	\N	71423c1d-54d4-4466-99a5-08a060820b6d
358	1	8v931h6IDci1/W7kbv/WI09snnL6QP+isS2vhUwrjTg=	bf30fbd9-0754-4c17-9858-05c95bf896d4	2026-04-23 16:44:49.988759	2026-05-23 16:44:49.988954	\N	2026-04-23 17:53:12.148056	\N	17bf74eb-3003-4b64-a855-f5c18fdb6ed6
370	7	1iWb7YG4DxQOxUdQGioksTOGKtFfP83iSPchuVNwOYI=	5599083a-64a2-43e0-96e5-6cd5909040d3	2026-04-24 11:05:33.876459	2026-05-24 11:05:33.876514	\N	2026-04-24 16:22:18.803954	\N	31dc56e8-6552-46fc-ab24-291cc5ffa2b7
371	7	U4SbInEJgdthhqWRZO01f2GZ9hmN1zgU5X0ZDBPgKPw=	6a4e4a21-871a-4357-8c8f-aeaa444b8207	2026-04-24 11:24:25.097074	2026-05-24 11:24:25.097565	\N	2026-04-24 16:37:58.35278	\N	6947e259-ac32-4a71-a4c1-35a71939b15a
14	1	CIwUydOK2QLxElGUfrCfFIJe+Sni7kXIK5pqWxS6upY=	54e11445-b1b3-43a0-b6ae-98e4efa0e977	2026-03-06 08:33:17.935761	2026-04-05 08:33:17.93582	2026-03-06 18:28:19.484405	2026-03-09 21:07:54.731863	15	735c990b-f83b-4071-911a-6210a7e5e003
422	1	z3yiVTJlKECXxkI1lXxG+LWTsLkoVAwmI0qquGvYLwo=	9bc85ec5-34d9-4d74-9f37-0a0f4992d1f1	2026-04-28 19:24:49.062447	2026-05-28 19:24:49.062498	2026-04-29 07:54:52.27538	2026-04-29 08:20:56.418468	423	fd0bc5a9-8018-4b1d-8ea3-caebd1b07018
23	1	iYNu2GZmA3l2niTh8AaQGpeT0y1I+PUnPczXkZCC/l0=	05054184-e612-4f43-be9a-6dfda8e6d9dc	2026-03-08 12:15:55.921881	2026-04-07 12:15:55.921896	2026-03-08 12:43:38.916229	2026-03-09 21:07:54.731863	24	735c990b-f83b-4071-911a-6210a7e5e003
379	7	TLGKeWT5ECiozMEEdeKbbXmc/lIHUu1i1oke7jx2KKw=	cd9e2185-5a99-4253-98aa-5c3610daf428	2026-04-24 16:37:58.893829	2026-05-24 16:37:58.893903	2026-04-24 17:08:09.260426	\N	380	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
15	1	duPaPhPs+YE2bd/kWSTLkJtyvdf8F0hp3/9SIFpZZXA=	8e8f5b76-5fad-4190-9d7d-13ed1c70aa73	2026-03-06 18:28:19.466849	2026-04-05 18:28:19.46687	2026-03-06 18:55:53.354333	2026-03-09 21:07:54.731863	16	735c990b-f83b-4071-911a-6210a7e5e003
388	7	9/NE7szDtcVuB2DaXvZNEaGQYsMRF1eCOTKizc+2Hps=	8ef7527b-4e66-48f3-a7d7-7b0aac768f3b	2026-04-25 08:45:40.702014	2026-05-25 08:45:40.702105	2026-04-25 10:40:14.976407	\N	389	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
396	7	op5lhQcTaUeTiISZomF2vCouc09g6FgsKVmvfjWjTQQ=	8bd4250c-e163-40b8-94c3-ce1cfc668e18	2026-04-25 15:56:59.631673	2026-05-25 15:56:59.631759	\N	2026-04-25 16:23:34.680333	\N	81b8729c-bae7-4de9-84ac-1f718d8b39a0
426	1	nTRTy8b8f9Gz6WI26TM4hCZAjFf0WtVbuv5sQoFxrlA=	9fb2a583-13b4-49bb-9dab-bd334cb540e1	2026-04-29 13:45:21.895596	2026-05-29 13:45:21.895758	2026-04-29 14:02:39.983552	\N	427	1ec4f6f6-b212-471a-9704-f8689e11e9b7
403	1	GZMRv+jhuzoM8DRc2mXKRovScLQ3h+Z5x6wccUJcDow=	33cb80cf-77f0-4056-ab69-0a1992f5ff96	2026-04-26 11:17:33.320369	2026-05-26 11:17:33.320508	2026-04-26 12:10:42.773135	2026-04-27 03:08:18.904544	404	515d4f3d-f44c-4dc5-9570-1891b51a79c4
411	1	eVoLH/S/ActolnubcCnxJ4WPHB/N6/+Wrf27B8sNc0A=	c46e0813-f4eb-4391-8967-4a9028d6b090	2026-04-26 17:30:51.055767	2026-05-26 17:30:51.05584	\N	2026-04-27 03:23:10.616604	\N	0a332d4c-c37f-4920-913d-ae884e452004
36	1	ttgxdyWz8ws208xyOmEYsqj3QDyrE3SSKerey4Gk0Vw=	9af00a2b-5cc9-4d3f-ba6e-828899431f1b	2026-03-09 09:30:45.840681	2026-04-08 09:30:45.840733	2026-03-09 10:15:47.72039	\N	37	3fcd1356-e312-4ac8-8c8e-4472330d8ec0
433	1	EWVokzC0sz4vHpX4DZ5tryCLGYP9BI5I8Pha/zuWnmY=	36f53294-c3cc-4a17-9cff-e4371b9497bf	2026-04-29 16:33:53.091012	2026-05-29 16:33:53.091132	2026-04-30 09:03:35.907818	\N	434	1ec4f6f6-b212-471a-9704-f8689e11e9b7
434	1	631mU/vtTHRmU0bp4feKch/XOSsQFTzR0w4WGhKVhfk=	794d67fc-f085-4217-aa8d-433a074b7e1a	2026-04-30 09:03:35.908083	2026-05-30 09:03:35.908129	2026-04-30 09:25:42.682981	\N	435	1ec4f6f6-b212-471a-9704-f8689e11e9b7
440	1	1rZjeP5A0CGSh7a1nYBe2Wp5mcG4ccpgknfrUht3ZOs=	8a53c819-b259-4524-9aa2-5074ff801dd1	2026-04-30 11:00:55.008659	2026-05-30 11:00:55.008774	2026-04-30 11:20:25.205074	\N	441	1ec4f6f6-b212-471a-9704-f8689e11e9b7
445	1	Rs6vSnxBPuBIMcVqJIhf+HXRK9JIf05+LkflC37CaQo=	ac75f584-576d-4bd3-acde-000cecde7a09	2026-04-30 12:55:49.448053	2026-05-30 12:55:49.448122	2026-04-30 13:36:55.564291	\N	446	1ec4f6f6-b212-471a-9704-f8689e11e9b7
455	1	xGY2sb7ilKeVWkaEOoOXHDTku2PnwH7gICm8SgBS9KY=	6913ad35-5835-4285-ac92-6cab8385c9ec	2026-04-30 19:43:03.079172	2026-05-30 19:43:03.079257	\N	2026-04-30 19:43:06.702185	\N	1ec4f6f6-b212-471a-9704-f8689e11e9b7
456	7	hooV/aPsS11/OOxFcTBQDHe/C1JYvPaYVMNHRRsEbC4=	fe19c458-4da3-446c-9482-03d66bd5dba9	2026-04-30 19:43:12.154622	2026-05-30 19:43:12.154623	\N	2026-04-30 19:44:20.16408	\N	9685a61f-887b-4a3b-9619-397e067bcb32
462	1	JT5piURfwzuA9I2XkLdWL9obytAX0ZtZ6w+FwpCZVWw=	6b78ff42-8516-40a0-8fea-6b4248c13680	2026-05-01 15:26:04.972213	2026-05-31 15:26:04.972297	2026-05-01 16:02:07.278892	\N	463	04dfdda0-3bd5-48d0-9600-8100cd554987
467	1	r8SRJ9Ag+wFnZkansetFYSbuy/mY7IEmgg5Ue7RAP7s=	a989bda2-2631-4ffc-8f1e-f8f2c410f867	2026-05-01 17:28:15.461357	2026-05-31 17:28:15.461396	2026-05-01 17:49:17.783364	\N	468	04dfdda0-3bd5-48d0-9600-8100cd554987
471	1	Uv6WUn4DxZEoEiMNAK3SL+Jbl7b3wdiT/sDn5iQHL2Q=	475e2ac9-ead8-4efd-9b8c-7e5b166496a5	2026-05-01 19:37:40.200291	2026-05-31 19:37:40.20036	2026-05-01 19:52:58.453111	\N	472	04dfdda0-3bd5-48d0-9600-8100cd554987
475	1	8jHlbkYjK93LoeYOXThaCAVkaXaLtE0Gbq2Mrak5wz0=	aad07386-7dce-4658-8167-d9e8613ee743	2026-05-02 08:03:28.660959	2026-06-01 08:03:28.661033	2026-05-02 08:40:45.405208	\N	476	04dfdda0-3bd5-48d0-9600-8100cd554987
34	1	cmjzziGat4eRVD7ko4VBmCBF8r+GFutam6a1J/zHIBw=	16b6811c-b8d1-41a1-9b45-a6a9da705ae8	2026-03-09 08:32:56.59598	2026-04-08 08:32:56.59602	2026-03-09 08:57:47.577946	\N	35	ebe45d87-add2-4aab-a5a6-cd4e7e38b008
479	1	Re7lCOlz52d7kznTkYYw/nMY/Okg0A6LSWeLJ1blB0A=	ad7690ab-9de7-46d1-948b-a3d4f2ede563	2026-05-02 11:01:49.156742	2026-06-01 11:01:49.156809	2026-05-02 11:34:46.495981	\N	480	04dfdda0-3bd5-48d0-9600-8100cd554987
483	1	QWnWmAMT6AZZ+2p8T69JaNeiDno+8EXRtpjJ77b1HDc=	97b25c25-e4e3-4bf6-8363-d85b4a9da44c	2026-05-02 13:00:46.487009	2026-06-01 13:00:46.487039	2026-05-02 13:16:22.237123	\N	484	04dfdda0-3bd5-48d0-9600-8100cd554987
38	1	OWg2P0vEuIu42FJKDLJOmE395Mwg30gn2WJ6QRk2upY=	b72a2c3a-b7f4-4add-bea9-5a241db04f7b	2026-03-09 15:55:06.704116	2026-04-08 15:55:06.704166	2026-03-09 16:11:26.792039	\N	39	71423c1d-54d4-4466-99a5-08a060820b6d
5	1	gxYloGRh8E+H1/pd+FKKB1547wrlOtAYOBMWzXJ9uU4=	a4b2069f-2980-4dd9-b163-bcfb1913e0ad	2026-03-05 13:23:27.622872	2026-04-04 13:23:27.622927	2026-03-05 15:22:25.261067	2026-03-09 17:01:49.701103	6	7a805761-7681-4145-b991-4eba9856c6f1
39	1	KaDyrVppZaJaoaR7OhPm6b2EKdYjEoWZ+I20cBoWo/0=	541e1a36-c877-498b-9719-0769edb78ded	2026-03-09 16:11:26.792205	2026-04-08 16:11:26.792222	2026-03-09 16:30:21.929324	\N	40	71423c1d-54d4-4466-99a5-08a060820b6d
6	1	yw8viEs9YGlLxVrXNMEdYNzw+dRqL/Fu7Judvj7wTBg=	82d1a23d-c1e7-4eb0-b82a-27464c0d5570	2026-03-05 15:22:25.230142	2026-04-04 15:22:25.230182	2026-03-05 16:12:32.319857	2026-03-09 17:01:49.701103	7	7a805761-7681-4145-b991-4eba9856c6f1
8	1	EE6u2beHrkWbRMUa9yXZzF1VqZ84If8Ylef07QXvmqk=	c97bc3fe-e728-46da-b6ce-2af5f3569d72	2026-03-05 16:53:56.63287	2026-04-04 16:53:56.632908	\N	2026-03-09 17:01:49.701103	\N	7a805761-7681-4145-b991-4eba9856c6f1
29	1	g6WeJGRYxUuGZUdMkpek1v9ZWMvp66j4LHPLDnA0Rec=	39d086b4-eb9c-4e89-9823-48debb27317b	2026-03-08 17:46:02.403229	2026-04-07 17:46:02.403258	2026-03-08 19:41:09.570717	2026-03-09 21:07:54.731863	30	735c990b-f83b-4071-911a-6210a7e5e003
7	1	mAIwQjaD4yum9b+U8vWZ8gDnB70SHCnew+V762A6yvU=	a19e8c27-8916-41ec-981b-ac947c4960ba	2026-03-05 16:12:32.289747	2026-04-04 16:12:32.289793	2026-03-05 16:53:56.661864	2026-03-09 17:01:49.701103	8	7a805761-7681-4145-b991-4eba9856c6f1
16	1	pmc8s3QLcDWESdZriMWRXsLleiPEIzaIQ218uahKgPY=	08328e6c-377c-4711-85e2-a45bb422535a	2026-03-06 18:55:53.330132	2026-04-05 18:55:53.330161	2026-03-06 20:28:08.404262	2026-03-09 21:07:54.731863	17	735c990b-f83b-4071-911a-6210a7e5e003
17	1	9S+9wltSR1jMX4N0dDIG2GHr2G8VjwsphmAXhn8FRLo=	bb99ee10-3f49-460b-9b72-e0fb8b70e2fa	2026-03-06 20:28:08.372694	2026-04-05 20:28:08.372728	2026-03-07 12:03:42.428862	2026-03-09 21:07:54.731863	18	735c990b-f83b-4071-911a-6210a7e5e003
9	1	K7eIX5ka3D3ARknadgRtjOfJc0I4hwaIqwBUZJe9gVY=	dce25d12-6919-4568-b4b0-f3227c65c17e	2026-03-05 19:35:02.560254	2026-04-04 19:35:02.560376	2026-03-05 20:07:20.559656	2026-03-09 21:07:54.731863	10	735c990b-f83b-4071-911a-6210a7e5e003
24	1	lZv4fJmcMxrQGNnqZB/e1my76MV8lMXKTUVcK0jQi2A=	8bd2cea4-631b-44e0-a3e3-ffb3a1e4edf8	2026-03-08 12:43:38.91637	2026-04-07 12:43:38.916389	2026-03-08 13:29:24.904107	2026-03-09 21:07:54.731863	25	735c990b-f83b-4071-911a-6210a7e5e003
10	1	Bg+yRi+REu8Z6A4lqYYAVdJN3sdY85NCvbIRcAH2ye8=	8cddf66c-a341-4a61-b2d2-582ce3474b82	2026-03-05 20:07:20.540353	2026-04-04 20:07:20.540386	2026-03-05 22:57:07.40873	2026-03-09 21:07:54.731863	11	735c990b-f83b-4071-911a-6210a7e5e003
11	1	mLEkzUXQXhc4tMvr4JLs2c1WOFaeM/S3lw94ZWt+Z24=	eb3c1c2c-74f7-4731-b576-2792944d51d3	2026-03-05 22:57:07.391378	2026-04-04 22:57:07.391422	2026-03-06 06:26:12.263837	2026-03-09 21:07:54.731863	12	735c990b-f83b-4071-911a-6210a7e5e003
18	1	K4+nhEABKOXgs0gE0GJjGwdlL8Lu7D9in75Pvx9YJPc=	3046b1bc-02ef-4609-b34e-ce815fc7591d	2026-03-07 12:03:42.401694	2026-04-06 12:03:42.401736	2026-03-07 14:17:04.898107	2026-03-09 21:07:54.731863	19	735c990b-f83b-4071-911a-6210a7e5e003
12	1	tD0g663dMAJ+HxzHpJ7MONQ1XtLuurtaLRi9LQ3r3Qs=	102af01e-337c-4bc6-a47f-62c4b3e7a046	2026-03-06 06:26:12.246303	2026-04-05 06:26:12.246332	2026-03-06 06:42:56.971896	2026-03-09 21:07:54.731863	13	735c990b-f83b-4071-911a-6210a7e5e003
13	1	5vpm8NCQjPO6y8aqyIUVAPzkLmSdOg7XD0Ke0BWSA8Y=	1f4c231c-6107-46dd-ada2-bb3f1eeb7e75	2026-03-06 06:42:56.954211	2026-04-05 06:42:56.954239	2026-03-06 08:33:17.960501	2026-03-09 21:07:54.731863	14	735c990b-f83b-4071-911a-6210a7e5e003
19	1	OmQprgW5eFD7X2wDEBc0xo5Iozsr3pj0Hq+owcEH4Ks=	e5bb8306-332e-4685-b09b-0022650ae793	2026-03-07 14:17:04.873334	2026-04-06 14:17:04.873385	2026-03-07 14:44:20.432999	2026-03-09 21:07:54.731863	20	735c990b-f83b-4071-911a-6210a7e5e003
25	1	Wrptor6YyItaG0rRmLeiX3UarakAuwjQ6KqoO4SmiCs=	a99593b5-1f31-4c77-8f6b-c1a5b5108c49	2026-03-08 13:29:24.904304	2026-04-07 13:29:24.904333	2026-03-08 13:47:46.714998	2026-03-09 21:07:54.731863	26	735c990b-f83b-4071-911a-6210a7e5e003
20	1	H9/a3j5aWFX0YE4DIrK0xeMZyZRv9ZP8EWF2/5E9kIA=	91e70bf7-12a4-43a3-b8a1-b0ab90aba95f	2026-03-07 14:44:20.40801	2026-04-06 14:44:20.408038	2026-03-08 07:18:39.133956	2026-03-09 21:07:54.731863	21	735c990b-f83b-4071-911a-6210a7e5e003
21	1	OvCEOQ6Ei3SNQ4ZfJF4XvWfRctbHz0W832SBii4VVYo=	fafb0ce6-8dd2-42f1-bf5f-a002462f6613	2026-03-08 07:18:39.113503	2026-04-07 07:18:39.113534	2026-03-08 11:51:30.221095	2026-03-09 21:07:54.731863	22	735c990b-f83b-4071-911a-6210a7e5e003
22	1	EKpwVxhlLpr6TsJSCd8VsF5KS8fydnjF+sJqj1pKd80=	4b680d18-f5bd-4b58-875d-0ed16dbcb275	2026-03-08 11:51:30.221519	2026-04-07 11:51:30.221562	2026-03-08 12:15:55.921774	2026-03-09 21:07:54.731863	23	735c990b-f83b-4071-911a-6210a7e5e003
30	1	O/5dVNQRfQX4lhw76Cxf60WB1vaNbbipKVW2HL/i0JM=	2bb5bd87-4e6a-4ba4-9636-996f0838d331	2026-03-08 19:41:09.571081	2026-04-07 19:41:09.571135	2026-03-08 20:03:49.401102	2026-03-09 21:07:54.731863	31	735c990b-f83b-4071-911a-6210a7e5e003
26	1	uXeYSuMhmXZgK0jEokiK2GvBOZyrJKcO4FSds5Y5ZH8=	f7e96fc9-7e3e-4b89-ad05-116b9633e8ed	2026-03-08 13:47:46.715259	2026-04-07 13:47:46.715295	2026-03-08 15:04:37.255095	2026-03-09 21:07:54.731863	27	735c990b-f83b-4071-911a-6210a7e5e003
27	1	70akCtRZTR7DS9H20jzMcZEnqyTQeX/l/d9akwqDTLo=	4f025f85-4f5d-4456-adde-8542b9d0dd02	2026-03-08 15:04:37.255287	2026-04-07 15:04:37.25531	2026-03-08 15:57:35.999336	2026-03-09 21:07:54.731863	28	735c990b-f83b-4071-911a-6210a7e5e003
28	1	NRWiS0V84rhAjc/Lbn3qrBqba9vqSl+IYadMH2lAR4k=	e5900621-a8fc-4104-a7c2-10796a8a01c0	2026-03-08 15:57:35.999535	2026-04-07 15:57:35.999568	2026-03-08 17:46:02.403032	2026-03-09 21:07:54.731863	29	735c990b-f83b-4071-911a-6210a7e5e003
31	1	2+SKXEBTQFARj/umy8/R1hDyEdVdnyGEeOEQii6zyg8=	66d5b001-fd13-417d-9398-e87b26f6ee54	2026-03-08 20:03:49.401111	2026-04-07 20:03:49.401111	2026-03-08 21:36:18.806178	2026-03-09 21:07:54.731863	32	735c990b-f83b-4071-911a-6210a7e5e003
33	1	JRYLDhuRnl6se9FRMEnL50ItXZscLwoalfy5ejX2XAU=	266abd6f-4253-44b8-92e0-ca647b0ea1d9	2026-03-08 21:50:56.768474	2026-04-07 21:50:56.768513	\N	2026-03-09 21:07:54.731863	\N	735c990b-f83b-4071-911a-6210a7e5e003
32	1	MYWn15RXD88nCCZaekTJxJuxUC4JhUeutEqu+ZlPfLw=	6011902b-f7d9-432e-8464-9c6334184526	2026-03-08 21:36:18.806376	2026-04-07 21:36:18.806409	2026-03-08 21:50:56.76825	2026-03-09 21:07:54.731863	33	735c990b-f83b-4071-911a-6210a7e5e003
46	1	5RXiKgiMUL0Ga3EIKCbuS8/nSi6uotK1hCAVpCGhQN0=	62555e06-641f-4ee3-9dfd-8b01e2a6fd70	2026-03-11 07:04:32.368312	2026-04-10 07:04:32.368335	2026-03-11 08:08:24.706112	\N	47	f8ff5e8e-82b6-4983-a60b-500a3f21ee08
42	1	j736zqwBaXdAL79gkf7WxmhcYrPAqcF69CnHUcq9Z44=	3a920c88-bcd1-4e4b-95be-f0d05b6a0f45	2026-03-09 21:07:54.928301	2026-04-08 21:07:54.928341	2026-03-10 07:51:18.940704	\N	43	f8ff5e8e-82b6-4983-a60b-500a3f21ee08
41	1	CPFvCZc8LILGnkxnKZxSH4ZT5FXHGzsPxo3/966WGb8=	b5603cde-c364-4363-88e8-e4597baf9e43	2026-03-09 17:01:49.86441	2026-04-08 17:01:49.864464	\N	2026-03-11 08:14:23.056586	\N	2c85bb5c-6d76-41f5-a8f2-80a7271052fa
43	1	ZVujb306ENR8oOXVYKevHeuhRYLRTAFzPw9nQWlir+g=	e552018f-9026-4454-a4f5-a8019fb2d5b2	2026-03-10 07:51:18.940917	2026-04-09 07:51:18.940948	2026-03-10 09:21:24.555794	\N	44	f8ff5e8e-82b6-4983-a60b-500a3f21ee08
47	1	MgJLKx4V0l2Jezcfbi7hLVmHLE3Lt/LZRu0s0akUgNk=	977d9946-4df2-4ea8-90db-08acd9e79e23	2026-03-11 08:08:24.706235	2026-04-10 08:08:24.70625	\N	2026-03-11 08:14:23.056586	\N	f8ff5e8e-82b6-4983-a60b-500a3f21ee08
44	1	S6+w5ynWXwZQvfUuB1OcRxshz1pb3Eq27wMkgg+Pqxc=	bb1649d7-124e-4d19-8568-6c6f560014b0	2026-03-10 09:21:24.556066	2026-04-09 09:21:24.556109	2026-03-10 21:06:08.952387	\N	45	f8ff5e8e-82b6-4983-a60b-500a3f21ee08
45	1	7YlpdS94f3i4wiA4/8pWIKd2oZX0mQ4o6rOBXK0a6WY=	715b4e4f-e4e0-419c-a70f-7f8ef2155c9f	2026-03-10 21:06:08.952534	2026-04-09 21:06:08.95256	2026-03-11 07:04:32.368092	\N	46	f8ff5e8e-82b6-4983-a60b-500a3f21ee08
51	1	x5Y2POmiZF7a4MHXrlA5DbWJe+AJZmwHtnxbve9rIwg=	fc32468b-8101-4e23-91de-bf0be1a6898a	2026-03-11 20:35:35.174844	2026-04-10 20:35:35.174877	2026-03-12 05:41:32.049344	\N	52	14510f9f-8e5e-42c7-954a-3212c40729bc
48	7	8Y9AR6vuTOFOIvcl2xTh//ug5+UKVPn3p9oCm/r99R4=	4db016e9-87ff-4f28-be4f-df9140cb88e7	2026-03-11 08:14:27.881104	2026-04-10 08:14:27.881157	2026-03-11 08:29:47.582971	\N	49	ebdcf2f8-0a7d-4fdf-b213-744b5dfb8f4b
50	1	AYzUJZeYsAoehr4jKt9cOyHnzVACha3qOrAF1HSTpyo=	dd282f1d-f485-4d16-822a-4dc18e7be9cf	2026-03-11 17:56:28.648278	2026-04-10 17:56:28.648418	2026-03-11 20:35:35.17459	\N	51	14510f9f-8e5e-42c7-954a-3212c40729bc
52	1	xGTXcvVnmB5gy+49NruTeor8jvyJx9dPapbDkGqiy/0=	590b1e14-d5a0-4102-8b53-3ccff4a38bb4	2026-03-12 05:41:32.049617	2026-04-11 05:41:32.049672	2026-03-12 05:56:44.758828	\N	53	14510f9f-8e5e-42c7-954a-3212c40729bc
53	1	5M50GQFeloJdZ8IuI6YZG7vCEqFnEUdJ8F+H45IqFLE=	58065568-0415-4cb9-960f-b70909ef5839	2026-03-12 05:56:44.759079	2026-04-11 05:56:44.759126	2026-03-12 06:11:56.823332	\N	54	14510f9f-8e5e-42c7-954a-3212c40729bc
49	7	CZ24/Dpwlku9TYC12ww2r+l8ormaMVu+GCq6OPd/OPM=	f70c441c-4696-4790-8878-dea2ec27133b	2026-03-11 08:29:47.583186	2026-04-10 08:29:47.583211	\N	2026-03-21 13:39:13.66143	\N	ebdcf2f8-0a7d-4fdf-b213-744b5dfb8f4b
54	1	bfMhOyiFlTgE8o2DUF49pz8KP4YYDHYXgXllV/4LNa4=	0a367654-b00a-457a-8cd0-6aae628f2f1b	2026-03-12 06:11:56.823583	2026-04-11 06:11:56.823619	2026-03-12 06:27:18.817727	\N	55	14510f9f-8e5e-42c7-954a-3212c40729bc
55	1	yE631FeTg4J/YA1IuQB1+wwikZiYMRP2+93qD0e47Oo=	8628951b-308e-4402-b0c9-d07e9e28bf83	2026-03-12 06:27:18.817951	2026-04-11 06:27:18.817988	2026-03-12 09:26:57.868784	\N	56	14510f9f-8e5e-42c7-954a-3212c40729bc
56	1	fTFIw85ik73iJiM6F1AQPzCml9MzwYNylKWkZTClKFM=	cd86fe53-0d18-4fa5-8648-32fe0246bd7c	2026-03-12 09:26:57.86891	2026-04-11 09:26:57.86893	2026-03-12 11:09:32.51583	\N	57	14510f9f-8e5e-42c7-954a-3212c40729bc
57	1	8oGGS1LWFmjuOt+o743ZmJOxqAtng1MmP/p5UQi6eso=	a75d417f-8243-4a83-a7df-5e7ff3336ecb	2026-03-12 11:09:32.516189	2026-04-11 11:09:32.51624	2026-03-12 19:07:52.255956	\N	58	14510f9f-8e5e-42c7-954a-3212c40729bc
78	14	FGHe8Vg57bsOcvKoocqN2zpej3iShIM23CXgRCUbgCw=	aa434a45-d878-4119-988c-0922556d2915	2026-03-15 16:02:41.369242	2026-04-14 16:02:41.369327	\N	2026-03-15 16:03:21.876056	\N	6172ccd4-6f9c-4f3f-aeb0-f1e1d360aa08
58	1	k/tFYYpGr3mxszdpw5vTbRLlluiWH+m+nwb5ICFCEFs=	ee904459-7c4c-4ea4-8116-71355c09cc48	2026-03-12 19:07:52.256147	2026-04-11 19:07:52.256173	2026-03-12 19:24:01.049405	\N	59	14510f9f-8e5e-42c7-954a-3212c40729bc
59	1	RdS4GNRjepDAYhkHdXNJm+R4+39QlKPHKeR3XyZwMHY=	8dcbe4c6-7287-4d08-98d4-316c408c793b	2026-03-12 19:24:01.049703	2026-04-11 19:24:01.049754	2026-03-12 19:40:55.441637	\N	60	14510f9f-8e5e-42c7-954a-3212c40729bc
70	1	iKH8SLOX5/TT7SAFOVuXQlQBxZs5vSmae8jT9/n4JqY=	753ce78e-7672-4e2c-b6b8-2076391ce3c0	2026-03-15 07:33:27.596392	2026-04-14 07:33:27.596452	2026-03-15 08:11:17.00889	\N	71	14510f9f-8e5e-42c7-954a-3212c40729bc
60	1	NDwkI7SaEhls7Ge6DL1uLNIPOT3uVgH2ZfzYQWii0+c=	f823c039-5b27-4ade-a16f-5698c69d3b75	2026-03-12 19:40:55.441815	2026-04-11 19:40:55.441857	2026-03-13 07:19:43.272244	\N	61	14510f9f-8e5e-42c7-954a-3212c40729bc
61	1	su//DhLjToeBfxdu5QpikS/hkzKzNPX8x8tFIUdNNvA=	fa0b5412-5409-4631-b0a9-4dad2a97bfc6	2026-03-13 07:19:43.272509	2026-04-12 07:19:43.27255	2026-03-13 15:32:14.825658	\N	62	14510f9f-8e5e-42c7-954a-3212c40729bc
62	1	QiNryOr9zjSjPE/z1V/N0rZaYEhTSjfzQRJuyZAaHQ4=	15b9a332-c849-407a-90b7-5e3ba8734df3	2026-03-13 15:32:14.825979	2026-04-12 15:32:14.826032	2026-03-13 17:38:31.768361	\N	63	14510f9f-8e5e-42c7-954a-3212c40729bc
71	1	6RJf9Te23pZySEeMvnRk395ZXVz6u1GUD8Ji8FbbVwY=	8af82c8a-3761-4c10-8f0d-0303ab54fd2f	2026-03-15 08:11:17.00927	2026-04-14 08:11:17.009338	2026-03-15 08:42:21.848473	\N	72	14510f9f-8e5e-42c7-954a-3212c40729bc
63	1	TZyopzyVhJ0JdvUWHAeV3hw5tEwwvmQrPV6FommNhTY=	444c80eb-35ae-4960-9c46-ee243ac14d44	2026-03-13 17:38:31.768627	2026-04-12 17:38:31.768674	2026-03-14 18:23:14.642939	\N	64	14510f9f-8e5e-42c7-954a-3212c40729bc
83	1	jH16Lc7A3lID5CwE2Ngi8XGj9w2ytSw9019HYp3JH/8=	c398fbca-fde9-4712-8cd2-1fcdbbe3d7c1	2026-03-19 09:03:29.686002	2026-04-18 09:03:29.686028	2026-03-19 09:21:26.376093	\N	84	03a87634-9d2c-4437-8aee-aea0bf916ca5
64	1	YDha5gWn4IWFCHw2oQfeHFglXtjH3c/aV7hSXv70U/Y=	2fa7e591-6d7a-4572-94f9-16013e349156	2026-03-14 18:23:14.643377	2026-04-13 18:23:14.643446	2026-03-14 18:38:49.482182	\N	65	14510f9f-8e5e-42c7-954a-3212c40729bc
65	1	P0D6Yz40KL2rKI9Qnud5QMCgVXXHrcERdhqS1Sk9P5c=	2a587023-189d-4c83-bb72-7c62e02f0b89	2026-03-14 18:38:49.482433	2026-04-13 18:38:49.482473	2026-03-14 18:54:26.915648	\N	66	14510f9f-8e5e-42c7-954a-3212c40729bc
72	1	49cRQ9PqYAGRnUtef1uM36TPAILIzRpPvcbcOjvWpUo=	f6686ef3-3fec-466f-9588-7693d3e34a93	2026-03-15 08:42:21.848719	2026-04-14 08:42:21.848761	2026-03-15 09:15:34.863587	\N	73	14510f9f-8e5e-42c7-954a-3212c40729bc
66	1	dJaXD4SglK6kSyPSOmiXG+l7FK9P0VLhJP0+dV7MZ3M=	490275e8-ee7a-454e-a3b5-2d0669d259a7	2026-03-14 18:54:26.915997	2026-04-13 18:54:26.916048	2026-03-14 19:32:05.15827	\N	67	14510f9f-8e5e-42c7-954a-3212c40729bc
67	1	KYOXRBQ1odlwTMJBvEeD3Z7vXcmjf4nnmeDLrYVtwfU=	2519471b-392c-46a9-8f33-8eae29a4f752	2026-03-14 19:32:05.158484	2026-04-13 19:32:05.158519	2026-03-15 06:23:47.914418	\N	68	14510f9f-8e5e-42c7-954a-3212c40729bc
68	1	L2OpJBRNHtvGQQ1efUUJofNSk4ipMn1Ox5Drj7ARd44=	4ba62e52-7a18-4536-99bb-13ee2514e660	2026-03-15 06:23:47.914814	2026-04-14 06:23:47.914884	2026-03-15 07:13:01.888857	\N	69	14510f9f-8e5e-42c7-954a-3212c40729bc
73	1	oIraYFJKOD6wioVWJH3idsO4kD3hX880M+oQJ27hf1w=	497764d0-7e0f-469a-8cb2-c161b62d2341	2026-03-15 09:15:34.864375	2026-04-14 09:15:34.864513	2026-03-15 10:01:44.250996	\N	74	14510f9f-8e5e-42c7-954a-3212c40729bc
69	1	lr3gr0SuLHQM/2aeKyLn89EZJM5vnk4KiX7BIDPKOHM=	7cb153ed-47d5-4510-bb8d-0e2707c1845b	2026-03-15 07:13:01.888867	2026-04-14 07:13:01.888867	2026-03-15 07:33:27.596038	\N	70	14510f9f-8e5e-42c7-954a-3212c40729bc
79	1	fO4p148uUrESPQ61ngVggzcKbGHbrwcnl5UOEwRLifc=	5be46c96-3f7d-4599-93db-3e6604dc0f42	2026-03-15 16:03:25.690806	2026-04-14 16:03:25.690807	2026-03-16 12:46:17.596792	\N	80	03a87634-9d2c-4437-8aee-aea0bf916ca5
74	1	BjoX9NribjT+hyHLrVpJ7PtF8m1GLJ9Plc1+xDUVGkg=	e1dee023-7956-4b23-8682-3b34179085fb	2026-03-15 10:01:44.251211	2026-04-14 10:01:44.251251	2026-03-15 11:02:25.634613	\N	75	14510f9f-8e5e-42c7-954a-3212c40729bc
86	1	ZwgtfDRBKTSOnFJfVdTD86zKo/yq7/QUcs3+6VEjpXk=	2a7c746e-15fa-4321-8b5c-69c4e1b59ef8	2026-03-20 19:15:32.800782	2026-04-19 19:15:32.800816	2026-03-21 08:08:30.603733	\N	87	03a87634-9d2c-4437-8aee-aea0bf916ca5
75	1	mzHC26CNwNy44xqVgQE9pXQVOVp3z2v/malrLS2qtFA=	a6140272-7402-4907-bec2-6cbf98a052b8	2026-03-15 11:02:25.635007	2026-04-14 11:02:25.635067	2026-03-15 13:21:38.22569	\N	76	14510f9f-8e5e-42c7-954a-3212c40729bc
76	1	pO6h0b3tI+01oz0NjFWnRbDnISIMixBfd3W8MvEAmhU=	fc1e6126-f878-431a-a02d-44ea544806d2	2026-03-15 13:21:38.225942	2026-04-14 13:21:38.225987	2026-03-15 16:00:04.648119	\N	77	14510f9f-8e5e-42c7-954a-3212c40729bc
77	1	Ir7VbyCwCTofbJu8NbF/+2XfzQFR9he4B8ZnHmit23o=	be4f2ba4-93ff-46b5-b770-66c6387c3ec1	2026-03-15 16:00:04.648603	2026-04-14 16:00:04.648669	\N	2026-03-15 16:02:36.653151	\N	14510f9f-8e5e-42c7-954a-3212c40729bc
80	1	0fUEqmEhLAUmcrkV+TaYPKbqFvNa8z0iwlgGY79qF5Y=	39c0a051-bd8d-4c32-92db-e1d591970241	2026-03-16 12:46:17.597215	2026-04-15 12:46:17.597274	2026-03-17 08:24:07.00968	\N	81	03a87634-9d2c-4437-8aee-aea0bf916ca5
81	1	yNh2NaGAhGih1VTPPduADIv0tOSA0YLPrnxm4m0uom8=	263eb04a-bd71-40ac-8bdb-e8a14d79f87e	2026-03-17 08:24:07.009895	2026-04-16 08:24:07.009922	2026-03-19 08:41:59.046702	\N	82	03a87634-9d2c-4437-8aee-aea0bf916ca5
84	1	5UCszr7HHuNXWxwauUiROR2BMZNZ0585gDJ8A8k2h7c=	27e3ea5f-7dc4-4d5c-9903-0b99263ae1ef	2026-03-19 09:21:26.376102	2026-04-18 09:21:26.376103	2026-03-19 09:36:52.264388	\N	85	03a87634-9d2c-4437-8aee-aea0bf916ca5
82	1	p9OUfqo72wdVGbYUusuY3tdDeQVNmR+OQn0t5h9FXPk=	fded0d44-25e1-4797-a7d0-4b6688739350	2026-03-19 08:41:59.047143	2026-04-18 08:41:59.047209	2026-03-19 09:03:29.685747	\N	83	03a87634-9d2c-4437-8aee-aea0bf916ca5
88	1	rS/sLQY0ytAZTU/uAJaR9X9LUL0TjZjdXzW10WC0KYI=	e80629a7-6ee2-4a3f-8814-e9b951bb5f95	2026-03-21 08:26:58.449813	2026-04-20 08:26:58.449845	2026-03-21 08:41:41.85945	\N	89	03a87634-9d2c-4437-8aee-aea0bf916ca5
85	1	YrV3U64Gpqh6aquk7FkLzTc/r2VVd1gpovlr5OZSY6Y=	9e68c292-55f3-4892-925a-06c8e1fb01a2	2026-03-19 09:36:52.26476	2026-04-18 09:36:52.26481	2026-03-20 19:15:32.80057	\N	86	03a87634-9d2c-4437-8aee-aea0bf916ca5
87	1	KdP4r7wi6zoxiLAaGplpiIEacmBvHfxHd+4Z+OlW2pY=	648e792a-5563-42b6-a106-049fa9635f08	2026-03-21 08:08:30.604078	2026-04-20 08:08:30.604125	2026-03-21 08:26:58.449544	\N	88	03a87634-9d2c-4437-8aee-aea0bf916ca5
89	1	nFN9IUm8cxb0BexJiMxlKmaKBaqsDXklWLVSYLXYKFs=	0dadda67-d075-418f-965c-53446337b608	2026-03-21 08:41:41.859826	2026-04-20 08:41:41.85988	2026-03-21 13:35:04.452262	\N	90	03a87634-9d2c-4437-8aee-aea0bf916ca5
90	1	me+4HZqciO1475ZK7ls5XeMj76nJgrarXtAPA2XQWnQ=	1f4800fe-d043-4900-a222-8d9746eeff56	2026-03-21 13:35:04.452516	2026-04-20 13:35:04.452556	\N	2026-03-21 13:35:22.574111	\N	03a87634-9d2c-4437-8aee-aea0bf916ca5
91	7	b1KLzkrFYRDjOVsHgqxRjqeYUPnN/t+RQr1n4bRggZ4=	9c2ac6e4-ed35-41d1-896d-d81f530e64fa	2026-03-21 13:35:28.940576	2026-04-20 13:35:28.940577	\N	2026-03-21 13:39:13.66143	\N	f3cfa9b2-f1af-4a86-9c25-04685e62335b
92	1	JL3VGNYBbc1SypHSKnart8euDgZyf/k1iYeikFqoO44=	8621d5e8-d43e-459a-92f8-88de7225e5cd	2026-03-21 13:39:19.906351	2026-04-20 13:39:19.90643	2026-03-21 14:19:58.775065	\N	93	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
93	1	GOhwjFihf7J7o+l0mEBIGGqv0pTdD+B9VHW39ErICv8=	9875ac00-761e-4734-b87f-e46094457b86	2026-03-21 14:19:58.775344	2026-04-20 14:19:58.775382	2026-03-21 17:28:55.860857	\N	94	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
94	1	D/ESfj9Ggkay5luhGIDiURgbSt++9x7L3KqvWYHLBEU=	ac5eccea-0ea0-414c-93e9-938b91bdc325	2026-03-21 17:28:55.861233	2026-04-20 17:28:55.861305	2026-03-21 18:38:06.356857	\N	95	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
349	1	EdrNOcxNYA3RQ8d0U/qjvhoNZzOVRfaz1aoSiTCx1eA=	4f1a8dde-eac7-42c4-8c21-36fc27edded4	2026-04-23 13:16:55.779208	2026-05-23 13:16:55.779339	2026-04-23 13:36:13.391347	\N	350	68efd69b-5995-4fbb-83ed-b8706dacb092
95	1	WguJNRk2gcHajZwRFE8PknCcGme8oy8zSZ94nXEIrDk=	a27a7239-3135-4bfb-8a50-5f8aab6677a6	2026-03-21 18:38:06.357255	2026-04-20 18:38:06.357316	2026-03-21 18:53:25.979345	\N	96	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
96	1	VHndqMfTs8jMWA3CVGJOUS+TxeE0cFGd8l6Iq7ztxDw=	53ee4304-3dc9-49df-874b-ea6fa55daa18	2026-03-21 18:53:25.979983	2026-04-20 18:53:25.980144	2026-03-21 20:33:25.508926	\N	97	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
360	1	6o+KPfwNrgm1n5181ZQnZ0gk8j3V6i4MhaWvleP6B44=	66512d2c-b40e-4457-8e3c-e1f93e684d26	2026-04-23 17:24:30.29787	2026-05-23 17:24:30.297984	\N	2026-04-23 17:53:12.148056	\N	8ef7d39d-9221-4ed7-a5c3-0016388c7619
97	1	8kngjHzjSt//OKFay7VTqcB9xGqI0/EIW1gPjLXxfuo=	713cf2a8-bf30-4f71-b648-25abd836927c	2026-03-21 20:33:25.509243	2026-04-20 20:33:25.509298	2026-03-22 07:19:46.448252	\N	98	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
98	1	XmBtOvGfpECv9IjJfhgLwPDrZsbgfJPZEjXVvsWTVYA=	2637ddce-6e54-459e-af36-55029fb33df8	2026-03-22 07:19:46.448854	2026-04-21 07:19:46.448922	2026-03-22 08:42:44.496641	\N	99	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
435	1	7kjSilNXbAWPtTRV5mNgOohKocRvgsGYUXjhz3bFhNU=	9f5d4474-65e5-4601-95be-b6f6684f5715	2026-04-30 09:25:42.683512	2026-05-30 09:25:42.683583	2026-04-30 09:40:30.320177	\N	436	1ec4f6f6-b212-471a-9704-f8689e11e9b7
99	1	gbuFYKKbvn0ssbo5eD+q758OIMYeAXmQ1faK8Wrq9AU=	4e1121c2-027b-42f7-8555-32cd24d57b96	2026-03-22 08:42:44.497137	2026-04-21 08:42:44.497398	2026-03-24 15:19:49.514251	\N	100	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
372	7	dz82XHJcIQXo/ji1kVjaN3A4i/e+bq+8zEhl4xn08/8=	32f1ad9e-08ac-4615-97a6-cef9545ddef8	2026-04-24 13:07:43.702289	2026-05-24 13:07:43.702382	2026-04-24 13:24:16.11904	\N	373	0a0a76c4-576e-4558-a253-96cb5a168d30
100	1	+AyFvO5q/QutKC5x7n7a4YZaxZDZGmTNPEdk4tXUXo8=	a0af667f-6226-4b53-a209-130d5efc07a6	2026-03-24 15:19:49.514541	2026-04-23 15:19:49.514574	2026-03-24 16:41:12.386089	\N	101	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
101	1	KNnZ0TsiUoLuoWldTuQeqH/9sqXkDjZURyzzseg7E8g=	401d9561-57ea-406b-abde-5b6053872a92	2026-03-24 16:41:12.386299	2026-04-23 16:41:12.386334	2026-03-25 06:37:58.283622	\N	102	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
102	1	IiaYn446Wi/IkXvT5mEAHTMcNiT6xwa+lkPBT++yQm0=	88b20821-7ff7-4ecd-afa3-ae83ea5dc8b2	2026-03-25 06:37:58.283837	2026-04-24 06:37:58.283894	2026-03-25 06:53:17.651287	\N	103	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
103	1	RXWD1lqFadCGq30hxWmZbgvFygDMSzXO+7plJue+apM=	2b8985fa-e5f8-4eed-a5d0-0c3e7e72d031	2026-03-25 06:53:17.651553	2026-04-24 06:53:17.651596	\N	2026-03-25 06:57:00.310818	\N	8bbe5a0f-c93c-4388-94e3-df2ce1f10655
380	7	9rvlj36g8VRluk+PG4NvirwQLZh7eIL5NaLg9cgUOus=	afe5fcce-9dd5-4318-b303-889c7be91383	2026-04-24 17:08:09.261141	2026-05-24 17:08:09.261257	2026-04-24 17:30:09.710936	\N	381	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
381	7	EjikY3DqmJwUfrJSt48hx19Vq7CPKD34nxSTH7Nhgh0=	93b4f25b-b9c9-4720-9e93-3ded199e1530	2026-04-24 17:30:09.71147	2026-05-24 17:30:09.711555	2026-04-24 17:45:05.860055	\N	382	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
389	7	zO4shgMvN5XBNZzHUnbRb1VKUGo0QBoMX+AvJSugqe4=	6b589ba2-5840-4cc0-ab8f-fe78ba9b011d	2026-04-25 10:40:14.976953	2026-05-25 10:40:14.977026	\N	2026-04-25 10:48:04.501053	\N	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
397	7	ikc9oWOMp463OqNNypokgT9M0Ryjq0BU5+JAVlQ8P0A=	1fea9096-128d-40ba-9864-05d387deaef1	2026-04-25 16:13:36.806486	2026-05-25 16:13:36.806581	\N	2026-04-25 16:23:34.680333	\N	200afc3c-29c8-4e44-a677-69ec026f4a9e
441	1	clHUPOOYRtA3v5nL9ihbGOSJTcMikG/Yvqb5Iz/X790=	04c05c7d-7711-433d-916d-2bbfa504d33f	2026-04-30 11:20:25.205655	2026-05-30 11:20:25.205765	2026-04-30 11:35:30.472405	\N	442	1ec4f6f6-b212-471a-9704-f8689e11e9b7
446	1	fCc+y+dtTeZGy2wwLQs8DxcwX3Z8VKRIJ6lfAxWhvqY=	3f2484ee-8f20-40f6-9165-22b01267b252	2026-04-30 13:36:55.565028	2026-05-30 13:36:55.565115	2026-04-30 13:52:45.921209	\N	447	1ec4f6f6-b212-471a-9704-f8689e11e9b7
399	1	LRXXBoF7fu5SPxoTGNGZ9dlPsBGrQplnJv10GObP+Sk=	82078cdd-40d2-4638-af11-850821922b54	2026-04-25 19:29:46.014523	2026-05-25 19:29:46.014572	\N	2026-04-26 18:03:02.326391	\N	a2f1b67b-b33f-4851-9207-745e8365a08d
404	1	7JbiWQmT34cF17AeZyjwTmltVDaEHVlwWVP5t3vEW5w=	546a8e1d-a222-4838-8fc9-5c21ab96143d	2026-04-26 12:10:42.773147	2026-05-26 12:10:42.773148	2026-04-26 12:31:29.673772	2026-04-27 03:08:18.904544	405	515d4f3d-f44c-4dc5-9570-1891b51a79c4
405	1	Alnlr+nf6Ndh6B3GTjVfrGOiCurH/pva4nQEYG+vc9s=	7b4bcbd3-70c1-4ad0-b6b6-be42600bc274	2026-04-26 12:31:29.674228	2026-05-26 12:31:29.674298	2026-04-26 12:46:31.190578	2026-04-27 03:08:18.904544	406	515d4f3d-f44c-4dc5-9570-1891b51a79c4
413	1	x+IPSyOYRjguk3R91rrfPCIgXO60L2/Lt+uelD3YElg=	ac153cea-1c76-4607-81e5-d6edf7d805df	2026-04-26 18:03:02.701235	2026-05-26 18:03:02.701304	\N	2026-04-28 08:52:33.571035	\N	6a440623-95aa-4cbb-8188-b735a9571d55
427	1	DpoSDnhMlFVuNDkuOqCYeJ1WOWFuTPdLbtA6c3qdo5w=	57fceab2-8bdc-4663-b33a-5dd60e7c60ba	2026-04-29 14:02:39.984088	2026-05-29 14:02:39.984188	2026-04-29 14:21:13.929329	\N	428	1ec4f6f6-b212-471a-9704-f8689e11e9b7
428	1	Gk2FLKjO0iij89ouR49U9SF+QdtkEbqZEmlvIRhCtmw=	db7f890f-2e69-4b5e-9b15-69f44d83c6e3	2026-04-29 14:21:13.929815	2026-05-29 14:21:13.92988	2026-04-29 15:11:22.442077	\N	429	1ec4f6f6-b212-471a-9704-f8689e11e9b7
451	1	PPhuiLlmJFcoyzsan+NfQypHm/3DHY8HJ8rqNW4xLj8=	9e57bf54-f38a-4e21-b849-7b37bf325b90	2026-04-30 15:10:10.816542	2026-05-30 15:10:10.816607	2026-04-30 17:20:17.348378	\N	452	1ec4f6f6-b212-471a-9704-f8689e11e9b7
457	1	//eX0A8c41wpVGgW8U7VCdGFksT7NCkjQzb7hldB2u8=	4d7f8123-257e-4960-889b-b12d076ae233	2026-04-30 19:44:24.601946	2026-05-30 19:44:24.601948	\N	2026-04-30 19:45:22.130041	\N	cf141f74-f97e-47cc-b360-8910a606bcf9
463	1	HZ9G2K7g0lV1TDdIzN368miX1oQN2W2Oh9cTcvUFnBI=	8208c78d-a0d8-48c3-9122-58fa423efba6	2026-05-01 16:02:07.27978	2026-05-31 16:02:07.279865	2026-05-01 16:38:18.698317	\N	464	04dfdda0-3bd5-48d0-9600-8100cd554987
468	1	PU9Y1QRhsCcRTPePohVsKOmCJgmeWOe/wunPMFCPHms=	90389dd6-f3d1-4f9e-814d-d2822d0ddc51	2026-05-01 17:49:17.783374	2026-05-31 17:49:17.783374	2026-05-01 18:17:38.445049	\N	469	04dfdda0-3bd5-48d0-9600-8100cd554987
472	1	dfocuCIPPZRi3Zb8jIJXWhqIS0vDgkYXiWDonNlFMQ0=	ce760e80-59bb-4bdd-aa73-16b441edcc60	2026-05-01 19:52:58.453332	2026-05-31 19:52:58.453376	2026-05-01 20:15:29.268479	\N	473	04dfdda0-3bd5-48d0-9600-8100cd554987
476	1	mQhIO2TFbSCPlRyiqEsdzt/0vlWH8VqwyT4q1kLqeVw=	0ea8ca78-ea34-400f-965b-963209ae8ff8	2026-05-02 08:40:45.405508	2026-06-01 08:40:45.405549	2026-05-02 09:57:09.022807	\N	477	04dfdda0-3bd5-48d0-9600-8100cd554987
480	1	22emeyzZ8HUyVlQDluaCtcD3zoat71FDzAujUetqKyM=	6b43ede5-96bc-40ba-9d59-5d5ef78ffd7d	2026-05-02 11:34:46.496384	2026-06-01 11:34:46.496451	2026-05-02 11:51:10.882693	\N	481	04dfdda0-3bd5-48d0-9600-8100cd554987
484	1	Wq7JTpIJbBxNLXCVizBO2CevlzowD2e7Nbg5L/rqRwc=	41929ab9-9b00-4cee-a5eb-3b7e6551d563	2026-05-02 13:16:22.237942	2026-06-01 13:16:22.23802	2026-05-02 13:31:21.302947	\N	485	04dfdda0-3bd5-48d0-9600-8100cd554987
487	1	F02IihL+Lrgvkj8CCLndUQ2BV4HNwx+ozmuCJuWOvqU=	e1512f56-9a0f-4a4f-b7d2-3d601577fd66	2026-05-02 13:57:19.698585	2026-06-01 13:57:19.698585	2026-05-02 14:16:00.017747	\N	488	c60d868a-baa2-4585-be1a-7166d90e04dd
488	1	HoJFwmoCRxvQc0bvp8p0ymvA5nwIo8LIEPj87NI+EdY=	6d630dc1-1dc4-463d-b320-c7627bd0e693	2026-05-02 14:16:00.018039	2026-06-01 14:16:00.018078	2026-05-02 14:31:10.592689	\N	489	c60d868a-baa2-4585-be1a-7166d90e04dd
491	1	JqCKbULL8PLiqI65w8Dl3ApEwt1otiP3wGdLOOgPUXY=	8499b409-255f-447d-af6f-9b39dcde5730	2026-05-02 15:07:18.083956	2026-06-01 15:07:18.084015	2026-05-02 15:25:29.65864	\N	492	c60d868a-baa2-4585-be1a-7166d90e04dd
107	1	kVqZaZZtPBhu2/HaApYWYU++wdjzAbBYtQUNTW6wUfg=	c9a580f0-e6b2-4e0d-b9f6-73169ffd2789	2026-03-26 06:41:27.435412	2026-04-25 06:41:27.43549	2026-03-26 08:06:48.057597	2026-04-03 19:41:14.549993	108	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
494	1	igFO+ylznC3wiN3OJF/i2sAH7ZHpIADxQg1cwhZez/U=	02fe7881-402c-4fef-bdc3-000ab1e3c406	2026-05-02 17:51:35.872388	2026-06-01 17:51:35.872433	2026-05-02 18:58:25.299153	\N	495	8b40d916-37ae-44ac-b486-39436cf99e55
115	1	fSsawRYTingRc3mVI1hXoFQh89u70JFPmhkNDwmJVLI=	87e3cc1a-fcff-46f5-8aca-e54b23790c05	2026-03-27 10:10:35.872904	2026-04-26 10:10:35.872994	2026-03-27 13:33:11.434917	2026-04-03 19:41:14.549993	116	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
108	1	yCogjRCyCgr4p43S0PI0OyQmBTUuIFQH1D1BxhNiFKs=	7d32e6f6-f04a-4746-a2ee-0022a16dd22d	2026-03-26 08:06:48.058512	2026-04-25 08:06:48.058735	2026-03-26 10:45:19.82921	2026-04-03 19:41:14.549993	109	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
120	1	hQozZC5aJ28xLfx+RtIvzP/GGJJ1P6x39hTtZ3bmAG4=	8da6e14e-3f73-47d9-945d-1f32a3e0e67b	2026-03-30 16:33:04.328315	2026-04-29 16:33:04.32846	2026-03-31 05:28:22.520956	2026-04-03 19:41:14.549993	121	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
109	1	oms8L+nTQmnRxthmjq+F1TUyTE43TtHxqaM3Cy/KLAs=	46e00266-5f6e-42fe-b1b5-c27493f4d35c	2026-03-26 10:45:19.830052	2026-04-25 10:45:19.83014	2026-03-26 12:29:56.639976	2026-04-03 19:41:14.549993	110	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
104	1	mziHcOUkcvW9HXM46/jmxpfsT/gEAOPi9kJdOKtKsxE=	5b88a8c9-f1f1-408d-ae2e-b48e8a1716f4	2026-03-25 06:57:08.460211	2026-04-24 06:57:08.460247	2026-03-25 11:00:13.278174	2026-04-03 19:41:14.549993	105	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
110	1	kHzlc8w2XdEuSC2wtKmlBRyAEgH8Jdu7h2V29eAWPI0=	3a667623-382e-4b80-ac99-e5f8dc46188b	2026-03-26 12:29:56.640792	2026-04-25 12:29:56.640897	2026-03-26 12:52:50.938231	2026-04-03 19:41:14.549993	111	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
105	1	lYJaGH8qOxe+itkewUO2gWbF8Jz2nRc73RN/wA/EGHs=	fb0a6f47-808b-425e-8865-0f15fb230e05	2026-03-25 11:00:13.278514	2026-04-24 11:00:13.278567	2026-03-25 22:28:03.461924	2026-04-03 19:41:14.549993	106	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
116	1	PsI1+IHuK3sImVjE8g+4HoLerZqABhGepZusEBlH46A=	1357f64b-033e-4f4e-a851-4b47ec209529	2026-03-27 13:33:11.435888	2026-04-26 13:33:11.436035	2026-03-27 13:51:57.62925	2026-04-03 19:41:14.549993	117	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
106	1	T+MNZmY6QtCWtDfFgL9wxw19utFY3z9ej/reJgLpMoI=	82a82b38-141d-4a1c-ba5f-f3f2f75c6703	2026-03-25 22:28:03.462205	2026-04-24 22:28:03.462249	2026-03-26 06:41:27.434831	2026-04-03 19:41:14.549993	107	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
127	1	G4Od9yVM59173ZmJ1J2XgXIJ5I0e7wdHetbm5mXQzO4=	19fbf6e0-6349-4d75-bf55-6764f1f05ce8	2026-03-31 19:00:00.53274	2026-04-30 19:00:00.532825	2026-03-31 19:15:57.641209	2026-04-03 19:41:14.549993	128	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
111	1	flr+ndnV3GCBsBgJgIH8gqZRJlCwkOe4P5yxU6DJfgQ=	6584c244-3f40-47f7-86f1-beda41e2da37	2026-03-26 12:52:50.938946	2026-04-25 12:52:50.939036	2026-03-26 13:16:02.353037	2026-04-03 19:41:14.549993	112	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
112	1	i4aTE+FSwoeYUsFhG8gOaHpR5jxHb8YcOP/W7AxwYPU=	b9aa0e70-2da1-4508-be5a-b4cd6132af49	2026-03-26 13:16:02.353764	2026-04-25 13:16:02.353883	2026-03-26 15:50:28.214667	2026-04-03 19:41:14.549993	113	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
117	1	f+/I1Dlog7mGvH3vbHo5sfVJDj3lw7eg2buqw+nMVoI=	eab5e4b2-56bc-438a-9a7b-452ed1535fb8	2026-03-27 13:51:57.630183	2026-04-26 13:51:57.630299	2026-03-28 10:05:14.714862	2026-04-03 19:41:14.549993	118	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
113	1	trvTHeFkzWm6J6j5RnuNPVfOi71k65eq8gLsftTdmVU=	7accaf29-78da-4f9a-897a-e8de1870f081	2026-03-26 15:50:28.215551	2026-04-25 15:50:28.215691	2026-03-27 09:48:42.727979	2026-04-03 19:41:14.549993	114	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
114	1	VDPJ+2Tj+ruLpVM24udZ37DH21OhD7Rwob5+Iz8/7a4=	d461ee21-b50d-4fd0-9804-686ee7eeb774	2026-03-27 09:48:42.728724	2026-04-26 09:48:42.728912	2026-03-27 10:10:35.872373	2026-04-03 19:41:14.549993	115	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
121	1	t24D/WMozhyw4k09HKEM1saQxqphp0iyeH4meGPztk4=	246ba7d2-3df6-4317-9535-eb8672b0e67e	2026-03-31 05:28:22.522077	2026-04-30 05:28:22.522238	2026-03-31 06:28:50.944065	2026-04-03 19:41:14.549993	122	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
118	1	MN0bO7pECZ/wR/DT0UxDkiCBr2GeJJQOh5D0wFcTZ6A=	fb7a3966-b691-4d37-a023-a306310713cf	2026-03-28 10:05:14.716013	2026-04-27 10:05:14.716289	2026-03-30 16:06:36.598024	2026-04-03 19:41:14.549993	119	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
119	1	8dCDjgDhvOBsmMatnG9oIpy1zp9I6Przf/zCHIIi6mU=	b576a15b-9626-43a2-a741-9fcb0854f634	2026-03-30 16:06:36.598839	2026-04-29 16:06:36.599006	2026-03-30 16:33:04.327259	2026-04-03 19:41:14.549993	120	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
124	1	mRuVtzhBobj3rEKaaeCbhrDPOlBXEKzd7a4lATkUO2Q=	fd84d2fa-3bb6-4181-96a3-2eeb602fc0e9	2026-03-31 07:29:21.700947	2026-04-30 07:29:21.701032	2026-03-31 08:29:44.66065	2026-04-03 19:41:14.549993	125	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
122	1	H/7VuS4LcWpMynNfYCZJkR+BhMXrkieirBXphBQuNRI=	c10cff19-26df-430c-8295-d9bccffb7f51	2026-03-31 06:28:50.944866	2026-04-30 06:28:50.945041	2026-03-31 07:00:43.0104	2026-04-03 19:41:14.549993	123	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
123	1	qXXAeFYmXeJdUWx84a4mUkWqPMd/D6nxs6qImXmZM64=	471d82c6-9ffd-48ed-9c89-265d667b7747	2026-03-31 07:00:43.011235	2026-04-30 07:00:43.011364	2026-03-31 07:29:21.700434	2026-04-03 19:41:14.549993	124	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
125	1	3PideNvMr6RhO/cWn7efpc4P76iT7m7PN0tLIM9s2wc=	daa9b705-cd9b-4e76-ab12-c5df842b0ca0	2026-03-31 08:29:44.661362	2026-04-30 08:29:44.661582	2026-03-31 13:45:39.006534	2026-04-03 19:41:14.549993	126	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
126	1	w8aa04XNnYucK7726On7bv6Pwt3l1Xdqz/H6nNEQUKY=	466e61e6-681e-4f11-a334-cd7c0b4ce617	2026-03-31 13:45:39.007131	2026-04-30 13:45:39.007341	2026-03-31 19:00:00.532037	2026-04-03 19:41:14.549993	127	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
128	1	wlwu0xYZC0W0yFJzTGAV3FX2IeyahjHf07waL3Ek8cU=	a9abb5f2-6b74-41a3-9fa5-e3472bdc19de	2026-03-31 19:15:57.642329	2026-04-30 19:15:57.64248	2026-04-01 09:47:04.047361	2026-04-03 19:41:14.549993	129	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
129	1	INywk7/Yy0LhZfGsFwjPV+4V0iclpkyRWyZZfxDHHEs=	953c09be-a20f-4186-af83-3ef4b888ac41	2026-04-01 09:47:04.04821	2026-05-01 09:47:04.048291	2026-04-02 07:04:07.408534	2026-04-03 19:41:14.549993	130	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
130	1	ga0gp06N90FtOnmRsBwKOfWKXkmoItoeUZBTO+8/9Oo=	d630ebab-db86-483d-b994-867573279004	2026-04-02 07:04:07.409541	2026-05-02 07:04:07.409863	2026-04-02 07:19:12.01193	2026-04-03 19:41:14.549993	131	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
132	1	2ZUjpLJGfOEyfEz0zzDe853977tMdb/1XEMhecTmZ6c=	3a4c1582-dc83-4de7-8dad-01a2b98dda25	2026-04-03 19:41:13.546491	2026-05-03 19:41:13.546612	\N	2026-04-03 19:41:14.549993	\N	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
131	1	zkrj+v0enzaIHHvULfq73YCR9grJNlaM4f3QYtvItLU=	d3320ee2-447f-46a9-9054-a49d439ca64f	2026-04-02 07:19:12.013053	2026-05-02 07:19:12.013236	2026-04-03 19:41:13.545603	2026-04-03 19:41:14.549993	132	eb6dc5b1-17ae-4f1c-82f2-37b8f1fcd7e1
136	1	MqQe2lvzJRnoBO+vVW6RusBVImrtwPlPChTMoNU9RZc=	2462d4d3-0164-4e25-ad6d-974da7568d70	2026-04-04 05:38:40.457708	2026-05-04 05:38:40.457831	2026-04-04 06:13:24.65343	\N	137	9450eeea-55ef-4a36-a8f8-e0b68413968b
133	1	F0oawQaRKTscY+wihnCyFOOMk9gTrEp2gkr03hi+RHo=	165728e6-b1d4-48b8-9a10-461486cd2fb6	2026-04-03 19:41:21.757528	2026-05-03 19:41:21.757529	2026-04-04 05:37:18.627849	\N	134	47ac4bf7-9ff6-4d26-b62f-93159dee79f1
134	1	uIDUiRhLqyAl35l2gZzCW2c+54dLCeI1DNbMzl4dpGA=	7ef1a3b0-939e-4edf-a5c6-e28e141f0ddc	2026-04-04 05:37:18.628801	2026-05-04 05:37:18.628975	\N	2026-04-04 05:37:27.460029	\N	47ac4bf7-9ff6-4d26-b62f-93159dee79f1
135	7	jGLo/fo0p/8s2EoJSQEztU4vI3q5wU5EvBq3NYCctKU=	a1c74bae-69cb-46fd-8659-38f1b4861230	2026-04-04 05:37:37.870577	2026-05-04 05:37:37.870578	\N	2026-04-04 05:38:35.595114	\N	8a09e899-276f-4930-91c0-eaa3eb01e1f0
350	1	bfDLlP4A44v82JUzAdI9Hff6nMZHRK3ZjO0eWyoCZUA=	975c1026-c42a-4114-8eca-b115039e8598	2026-04-23 13:36:13.39234	2026-05-23 13:36:13.392483	\N	2026-04-23 13:44:37.906473	\N	68efd69b-5995-4fbb-83ed-b8706dacb092
137	1	0+yA4qO10k189D8kuaDsntZst0tveyX+tkNvpfv+nzk=	1b7af0b2-68ce-48c9-8965-96a5b216b1c2	2026-04-04 06:13:24.654503	2026-05-04 06:13:24.654632	2026-04-04 06:28:21.891416	\N	138	9450eeea-55ef-4a36-a8f8-e0b68413968b
138	1	53rCxr/JIh0b1ZKUrk1L2DbUxZ4cW4KBlIpi0BxWzzs=	ccc845eb-d39e-4c91-8777-ca3ac7144a60	2026-04-04 06:28:21.892272	2026-05-04 06:28:21.892377	2026-04-04 06:46:28.716376	\N	139	9450eeea-55ef-4a36-a8f8-e0b68413968b
139	1	Ij184UqqZauKKPkOT1mES6Df/WPy8dlWLdYTgriNApU=	2b9d297d-9ff3-4114-8256-16fc69d5582c	2026-04-04 06:46:28.717745	2026-05-04 06:46:28.717894	2026-04-04 07:03:17.561771	\N	140	9450eeea-55ef-4a36-a8f8-e0b68413968b
140	1	h7B7Un8Iv3a0/4SaFabz/Tv+U09bJ0PCExGredh/Msc=	ea7f437b-155b-44c5-8511-3db81629bc1b	2026-04-04 07:03:17.562448	2026-05-04 07:03:17.562546	2026-04-04 16:14:21.192317	\N	141	9450eeea-55ef-4a36-a8f8-e0b68413968b
141	1	+CONwVC7YFjBkIHtMM2E64NaBKwq5W3tHm0+U+S92KY=	0ac58468-f1ef-4099-9639-b684ee24c63d	2026-04-04 16:14:21.193129	2026-05-04 16:14:21.193216	2026-04-04 16:33:12.631771	\N	142	9450eeea-55ef-4a36-a8f8-e0b68413968b
142	1	XvROAsVLpgVxqOfzI3m0BXgCiMueipIeVnA/7UCDJio=	52c24f20-644a-4a2b-a59a-a8cf40f252d3	2026-04-04 16:33:12.632502	2026-05-04 16:33:12.632623	2026-04-04 16:49:09.125576	\N	143	9450eeea-55ef-4a36-a8f8-e0b68413968b
143	1	Z1log8qXC3nbH8Yzxo5wGveYtitgTduNb4GoJHOBH4s=	b61c2f90-95d7-4545-b165-bd683ed05b19	2026-04-04 16:49:09.12649	2026-05-04 16:49:09.126673	2026-04-04 17:45:15.130179	\N	144	9450eeea-55ef-4a36-a8f8-e0b68413968b
163	25	VkhC3xAkOUXi/MfdeJ/OgdoqT5jJ3dvm/GEHv60iOHg=	00bf46f1-aaea-4deb-8e05-a8488d54943e	2026-04-08 11:13:58.628433	2026-05-08 11:13:58.628434	2026-04-08 11:33:48.256917	\N	164	c46daf4a-bfcd-4032-a8ba-e4c6e4f01582
144	1	2VRI0ss+0e8oqRkWb0r2CA3gOQNSnyT1Xs3Bhw4fwBY=	4a981a4f-7d50-4cec-b924-0e74a66d3330	2026-04-04 17:45:15.130977	2026-05-04 17:45:15.131092	2026-04-05 04:19:00.673629	\N	145	9450eeea-55ef-4a36-a8f8-e0b68413968b
145	1	5fk9P2MLjUpujwafQOsfwowQlmpUO938nZQTi3Vo3TI=	dc3db7c4-ffb2-45ea-adc3-33b1c029f5ac	2026-04-05 04:19:00.674676	2026-05-05 04:19:00.674857	2026-04-05 07:10:44.012114	\N	146	9450eeea-55ef-4a36-a8f8-e0b68413968b
156	1	k0SvSLbLwU91LtpuwCBprQ1VMEgApPibk6xq/dRMjLc=	0ad08415-61f7-42b0-9689-9f6cd688a9b5	2026-04-07 09:14:06.148081	2026-05-07 09:14:06.148256	2026-04-07 09:54:02.597491	\N	157	9450eeea-55ef-4a36-a8f8-e0b68413968b
146	1	9o9/SJY6YnhWu3c7UhYH5qLzD0GzpGwhMDTrslR+N1o=	c12bec86-936e-476e-97a4-2c5df12d74b3	2026-04-05 07:10:44.012929	2026-05-05 07:10:44.013044	2026-04-05 07:29:01.561571	\N	147	9450eeea-55ef-4a36-a8f8-e0b68413968b
157	1	TfkqsYb5Skp8oY6NfLdywtoCmEEwN8m1/stV8nT9vOE=	01b1c884-eb38-40c5-b4c7-c644121b81ce	2026-04-07 09:54:02.598155	2026-05-07 09:54:02.598242	\N	2026-04-07 09:58:31.840947	\N	9450eeea-55ef-4a36-a8f8-e0b68413968b
147	1	C6Alk3lCnqW4ljSzf4CUx0SFQeAew9rxxNGnGyhcim8=	bb46cf18-7582-4c1f-8a79-1b7dd8e93c4f	2026-04-05 07:29:01.562475	2026-05-05 07:29:01.562609	2026-04-05 17:01:32.142803	\N	148	9450eeea-55ef-4a36-a8f8-e0b68413968b
148	1	knkywxdfQHubHmcj/JrbUFls14rihQBfgZRphtj4Hss=	48914a38-c066-484f-af49-f91b09ae2b88	2026-04-05 17:01:32.144046	2026-05-05 17:01:32.144226	2026-04-05 17:34:58.460248	\N	149	9450eeea-55ef-4a36-a8f8-e0b68413968b
158	7	jqPcrAADJGZbEqGRbxtK+daFOwSqp8FKn/Ob9uRPIXU=	8cec7d26-2437-40bd-82b5-b43d419fddd5	2026-04-07 09:58:36.215452	2026-05-07 09:58:36.215585	\N	2026-04-07 10:00:32.774186	\N	d1c5c43c-e871-4193-ba78-65db8d70d532
149	1	3yrld96mYvoomDfd9D3eX6GpIAPKbWecO3hbbz4uTk4=	1b8b6fb7-5f1b-42e6-8b0a-8c655bf325d1	2026-04-05 17:34:58.461196	2026-05-05 17:34:58.461317	2026-04-06 11:19:44.125243	\N	150	9450eeea-55ef-4a36-a8f8-e0b68413968b
150	1	EVICrgcgywkj6MlaEHo8bRzGdp/DP8Z3qLVtKdhprXg=	dc4d4d79-ac1b-46e3-bf05-bdbeada90778	2026-04-06 11:19:44.126373	2026-05-06 11:19:44.126635	2026-04-06 15:51:27.98325	\N	151	9450eeea-55ef-4a36-a8f8-e0b68413968b
164	25	sMpeUHiiMSKuKpRWRx6DJ/SC6rdrpbDiGserfwh9xQk=	cda7cd0d-7dc3-4eab-a430-37eb874fc451	2026-04-08 11:33:48.257445	2026-05-08 11:33:48.257554	\N	2026-04-08 11:34:03.847157	\N	c46daf4a-bfcd-4032-a8ba-e4c6e4f01582
151	1	X3gj5biwdMhQmohgG2e33vVlJ/fLPxJkYvZhLQ6fIw8=	e2bff2db-2ce4-4bd4-9e5d-94e0191122da	2026-04-06 15:51:27.983717	2026-05-06 15:51:27.983771	2026-04-06 16:14:04.163847	\N	152	9450eeea-55ef-4a36-a8f8-e0b68413968b
152	1	4TIQhO8mY0lB6rrwRAsFV2TIGybbFf1ePvPM2LQ+esE=	fb474c55-a18a-4a4c-b7f2-022b2dbcb69f	2026-04-06 16:14:04.164683	2026-05-06 16:14:04.16481	2026-04-06 16:31:02.465105	\N	153	9450eeea-55ef-4a36-a8f8-e0b68413968b
153	1	a/T+s6VjD2QJeT4PoSzpQ1AzXPqkgHGP5SAnEs8WFTc=	090d5729-1ed7-4f4f-8cea-4212a3675090	2026-04-06 16:31:02.465671	2026-05-06 16:31:02.465754	2026-04-06 18:41:48.913774	\N	154	9450eeea-55ef-4a36-a8f8-e0b68413968b
154	1	3F9sPLf2WBsBq7pvPGrH2tNz5TbTJ7KrVCJHy9YeVrk=	2514b1e2-eafc-41a6-96f0-12f5a3721d56	2026-04-06 18:41:48.91449	2026-05-06 18:41:48.914588	2026-04-07 08:58:44.191084	\N	155	9450eeea-55ef-4a36-a8f8-e0b68413968b
155	1	lUpSbqTbJGh9ALI1NYZ+MNNQI91+W9NmteK92Xd4mMY=	9dd3cd12-8297-4af1-9c79-76d66748a6e7	2026-04-07 08:58:44.191775	2026-05-07 08:58:44.191869	2026-04-07 09:14:06.147079	\N	156	9450eeea-55ef-4a36-a8f8-e0b68413968b
175	1	3fmnNhd0goUYDcGr4DHgTmUTXp0P13SCRnydxjdWvfw=	da30dc50-3b5b-4913-b02c-687cff0c202e	2026-04-09 12:27:23.863287	2026-05-09 12:27:23.86333	\N	2026-04-10 10:09:11.039479	\N	06ea1e1a-c937-4a3f-b8e8-777ad598cc3c
359	1	rLizdlu+n05Ld8Cxz/CWAxuinerxkkt1CB3zhkyuLS0=	9b2c425c-c3e5-4382-b484-78cfcafe2bdc	2026-04-23 17:02:18.646741	2026-05-23 17:02:18.646849	\N	2026-04-23 17:53:12.148056	\N	1c469a21-5ad3-4789-8394-14c8c1e8f73a
159	1	vzYEsCcmHIOgq7BGj4O+sezndiDVjfa17e0342EqJx8=	e17f17bf-8263-4ed1-876e-004143d0701a	2026-04-07 10:00:36.642629	2026-05-07 10:00:36.642722	2026-04-08 08:47:39.378385	2026-04-08 11:13:42.363788	160	2a872378-93c5-4dfa-825d-637c22e8ed59
160	1	PRhgN0jjCMevhDjBwY51z9oT166d6tjLrAGVV5XGnxw=	7a4ee3c3-caea-475e-81b9-fc2b06c48df0	2026-04-08 08:47:39.379287	2026-05-08 08:47:39.379404	2026-04-08 10:34:41.566191	2026-04-08 11:13:42.363788	161	2a872378-93c5-4dfa-825d-637c22e8ed59
162	1	Qd8rMIcxhnLiXFUsnZz/wDAVxwlLWBOpjy0c/Dy9tAc=	ce4b3c11-cdb9-4e61-ae0d-68bea88c651b	2026-04-08 11:13:40.700365	2026-05-08 11:13:40.700438	\N	2026-04-08 11:13:42.363788	\N	2a872378-93c5-4dfa-825d-637c22e8ed59
161	1	Y5wZvkqg01MrBgP46xXgzTfdoQl5br0E/hs73aEXYVE=	dd3beced-b986-4876-a544-6b089abdaa55	2026-04-08 10:34:41.567342	2026-05-08 10:34:41.56799	2026-04-08 11:13:40.699467	2026-04-08 11:13:42.363788	162	2a872378-93c5-4dfa-825d-637c22e8ed59
176	1	ANYNIf8b0UGb3N813HuUCkQN5kGilcQoFSS4cTb9ef0=	45b32319-862d-4621-8297-af4e15d73871	2026-04-10 06:50:15.614877	2026-05-10 06:50:15.614995	\N	2026-04-10 10:24:15.395989	\N	44108763-41cd-448c-8b76-a113ad66d218
178	1	yVfn4eCAJrFM2WHb+X5pGBP8yxGN7RjN7jsgA2crfBw=	dbeeb881-0fe0-42f2-bf06-f6aa4aa0f4ee	2026-04-10 07:06:29.026635	2026-05-10 07:06:29.026747	\N	2026-04-10 10:47:39.534353	\N	809f34de-bcdf-4bf6-be95-72543c68f4ec
361	1	GHY7GGjGbf+YhQ5/YA+j0AvdNlQGnANc5qqx2jqRuHI=	db152e0a-5394-4213-9e14-bb9e242a6346	2026-04-23 17:52:47.482749	2026-05-23 17:52:47.482879	\N	2026-04-23 17:53:12.148056	\N	f7f11939-6180-45c9-9fbb-dd5b6f39516b
351	1	GtrCnSWtfY+PCskkRVX8VykojKFssxJBYUl5yTdFFk8=	91569ff9-614e-48ec-a106-c1e52c4c281b	2026-04-23 13:54:36.241326	2026-05-23 13:54:36.241488	2026-04-23 14:09:52.853538	2026-04-23 17:24:29.709188	352	99e79b95-3389-4768-b970-dd8e7e7b8265
180	1	nPXhk5acZ+6powoyy3Uu0H41UgCXqpfXX8vqy0hnyo0=	94005f61-8836-43d2-8e7c-2015d1abb6c8	2026-04-10 09:35:27.124377	2026-05-10 09:35:27.124478	\N	2026-04-10 11:36:49.405416	\N	800c2cf9-2148-4bd1-9d90-ff459a26912e
166	7	Jvq5kdU5apBE9AtIZrOGX2IsKCYntvlPCF6ysuYV7Z8=	447d452b-73ae-4303-9c7a-da58dd9e6a9c	2026-04-08 20:31:51.889683	2026-05-08 20:31:51.889787	\N	2026-04-23 18:11:14.581345	\N	4ce5efa3-4792-4af7-9a0b-e48a133bd4da
177	7	b8Y9F5c8+PeSsTERlTvU0ShejB4JWshfwtvyIKlnUB4=	5985fffb-a1f1-45d4-9a00-20188aabfab2	2026-04-10 06:51:48.411018	2026-05-10 06:51:48.41102	\N	2026-04-24 08:03:16.198923	\N	94cb22c3-c66a-4be3-a0dc-283b3d1431a9
179	7	HLMxMcjWj+97eTaXOkCe8vPIzxkumSYuVI/q/U2KgNQ=	1e9142c9-4e76-416b-bdc7-9d44e73947b5	2026-04-10 07:06:30.771838	2026-05-10 07:06:30.771839	2026-04-11 15:05:57.230924	2026-04-24 10:00:45.627874	202	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
165	1	pXY5S3xmtOET5TyUJg4+mgH4eGHBvTY5YP4e4tirSSo=	cc2e8ac8-148c-4824-8363-d8003dc1e0d4	2026-04-08 11:34:07.877626	2026-05-08 11:34:07.877627	\N	2026-04-10 09:35:26.770432	\N	588f9781-fbba-4056-92a2-04d3059d7f55
170	1	h2keNCS+MqW0Fm2jXTtZC8qlpNzxChVYu7GuQv+sKsM=	e3c309ea-3694-493d-b176-56a2865e1222	2026-04-09 07:44:14.767619	2026-05-09 07:44:14.767732	2026-04-09 09:58:43.71931	2026-04-10 09:51:28.526945	171	376d438e-d207-4986-8268-ca26d0fd69e7
173	1	DPjhWt4su9f+kBX8OXJvR866RUD0g0e8+wXGl0QRBgo=	5440b23e-a89b-4a6a-80c9-440008f4ccb3	2026-04-09 11:05:20.359637	2026-05-09 11:05:20.359967	2026-04-09 11:38:33.758962	2026-04-10 09:51:28.526945	174	376d438e-d207-4986-8268-ca26d0fd69e7
373	7	DTK+nPU2bOXgBiyOEBUAtSH7/saqhWDO9OSQ6+4QhCM=	77d23ff8-13cc-416e-acc2-0f9136ca4e68	2026-04-24 13:24:16.120136	2026-05-24 13:24:16.120255	2026-04-24 13:42:21.805123	\N	374	0a0a76c4-576e-4558-a253-96cb5a168d30
382	7	ViT3fqVuMqZOCd6ApOF7tjrJKgbgWVTRoddlYPP+hJU=	6788fe38-706b-4a3e-b855-3e00d93811af	2026-04-24 17:45:05.86059	2026-05-24 17:45:05.860783	2026-04-24 21:56:49.552899	\N	383	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
390	7	EK7cdwNWY4VtEL4GoTHjOqIif2PYLPu2K/ddFzuvI1k=	69c2d7d5-e67f-4121-948f-bc530bdfde9c	2026-04-25 12:50:47.642659	2026-05-25 12:50:47.64275	\N	2026-04-25 16:13:36.570286	\N	cdaa0937-75fb-4af8-ad57-6ec6e5599656
167	1	P+3Ijc9k9yYet5m/xOpq3qSCCWedHHnkW+XxvAb5Gu8=	eac90448-40fe-45ac-bac9-b3d3a0874aa7	2026-04-09 06:53:01.000032	2026-05-09 06:53:01.000103	2026-04-09 07:08:38.946754	2026-04-10 09:51:28.526945	168	376d438e-d207-4986-8268-ca26d0fd69e7
168	1	326HxhvhVO0eqqAgiE5zkJXgZEYbVx9ZZJhbmJnGj9c=	fb0556bb-87dd-46bc-bdef-be3e989930c1	2026-04-09 07:08:38.94749	2026-05-09 07:08:38.94761	2026-04-09 07:24:22.411595	2026-04-10 09:51:28.526945	169	376d438e-d207-4986-8268-ca26d0fd69e7
171	1	DOs+5Y8JVpQ4KjyrC+VLUS5XWwS5bBvuT+1I9WYQcSA=	ccf04ca5-61da-4c64-ba2a-29957189e2dd	2026-04-09 09:58:43.720411	2026-05-09 09:58:43.720514	2026-04-09 10:15:34.900915	2026-04-10 09:51:28.526945	172	376d438e-d207-4986-8268-ca26d0fd69e7
169	1	raFuk0snrmgxUSMMI8+nx8HJ6m6I7Y5czEUXURG0vuU=	cf70db0f-841f-4424-b0f9-096a81744fac	2026-04-09 07:24:22.412361	2026-05-09 07:24:22.412447	2026-04-09 07:44:14.76682	2026-04-10 09:51:28.526945	170	376d438e-d207-4986-8268-ca26d0fd69e7
172	1	xMMD55qfa/J1a5SDz3AGVlLsOo7YLf8ykyE3FeZzaU4=	5c1092f1-1713-41fd-8b35-98509722c0b6	2026-04-09 10:15:34.90163	2026-05-09 10:15:34.901714	2026-04-09 11:05:20.358342	2026-04-10 09:51:28.526945	173	376d438e-d207-4986-8268-ca26d0fd69e7
174	1	jy0CGsz3FCrvjRPhN+Ob8ZfU32vIjNptPp/nAj0r6HI=	caa08435-4b05-4e6d-840a-25e35a3cdfaf	2026-04-09 11:38:33.759653	2026-05-09 11:38:33.759806	\N	2026-04-10 09:51:28.526945	\N	376d438e-d207-4986-8268-ca26d0fd69e7
181	1	ZeIWkNDVaCqz+2/m5i9BsqXf1pRoy3wzdlP3vvrPxhk=	037717a5-9ebe-4e09-9d22-3ef0148727fe	2026-04-10 09:51:28.911908	2026-05-10 09:51:28.912034	\N	2026-04-10 12:00:06.877343	\N	87309dc3-6ffd-47ac-a1c4-8a96fa74f40d
182	1	vsdNMajGag+yEUbsTzEA1zVhgO1FK3GeK/CusAkmikI=	108e5acc-347e-4fda-b57a-d293b369b09c	2026-04-10 10:09:11.440501	2026-05-10 10:09:11.44059	\N	2026-04-10 12:52:32.967193	\N	a481d4b1-7f1c-46b8-89b3-f67bc1af61f8
183	1	51U8ssESeboLfLf5pGv7blX2FvdPk7H2N30awZhvc4o=	a995de62-8097-4fc2-8f3b-269031fa0578	2026-04-10 10:24:15.763633	2026-05-10 10:24:15.763707	\N	2026-04-10 14:14:50.70551	\N	8b44e35f-53fa-44f9-a37b-8db75d7de918
184	1	eSDyr5FPjwD+8pLpammzXzv2eHW2T/SrICGV3z6eAZA=	9b6ce104-9275-4c5d-9d72-1ff115c0c7f8	2026-04-10 10:47:39.893799	2026-05-10 10:47:39.893957	\N	2026-04-10 14:42:13.826964	\N	27be0dc0-c6bc-433c-9122-cf3bd5f405b0
191	1	/kXp10db2ZJgSCp0CIUTc1FvRwlhJm+iyk5KRin+xr4=	76a021fb-b780-478a-91b2-a65faa04fec3	2026-04-10 16:52:35.03349	2026-05-10 16:52:35.033577	2026-04-10 17:08:19.984549	2026-04-11 12:11:13.095812	192	7de1b840-2f61-41c7-bfdb-62016259c7ea
192	1	7SRgmgy+jz71LydbMUwUv8T1XHCpfZg29bQfGmE3qu0=	ab8ec9c9-5e7b-4636-ad8f-4dc63f9d93a0	2026-04-10 17:08:19.985509	2026-05-10 17:08:19.985713	\N	2026-04-11 12:11:13.095812	\N	7de1b840-2f61-41c7-bfdb-62016259c7ea
185	1	xpcqStIYbjFcMpqryTT6/jVRJbZDG2ZcYHpDiHiZjS0=	c8aa6d03-999c-4ee2-8884-7da2ea9fc4be	2026-04-10 11:36:49.86293	2026-05-10 11:36:49.863115	\N	2026-04-10 16:52:34.633817	\N	6f40084b-6b87-4068-a1d4-5a4e4623c867
203	1	4hWVOKho/dKPyJe5Is94HiV3xA5TF9hi7ILT0HIdVnU=	45772c43-0ca5-4f35-9a3a-690091a6b7e6	2026-04-12 12:16:56.016865	2026-05-12 12:16:56.017022	2026-04-12 12:37:43.18609	\N	204	9d2cfa35-90da-4102-bc5f-57ea8d69b94c
186	1	Mkao+pdluoAAG9z1I8390oVi1vL/nKjdVb1qrx+35DA=	7c2c4598-4b13-476d-b5bc-bdb0b34d8d64	2026-04-10 12:00:07.335027	2026-05-10 12:00:07.335167	\N	2026-04-10 17:23:36.625328	\N	672a0317-7e7c-46f3-82d4-0efc7691cf57
187	1	2B/UGoxaRNt5GNNNlnvRqFIoh3O09yclA7AJZpFf/Kc=	b9ea5120-524f-4f7a-83b9-c5d23ec9d2d0	2026-04-10 12:52:33.370522	2026-05-10 12:52:33.370612	\N	2026-04-10 17:41:02.105085	\N	66381d77-618c-40d6-882e-19e570691981
188	1	zYgvHsorGIbpdYJEq7IInbciiN11i6ZcIrLMCy8sZ1Y=	b221e34b-802f-4f60-8057-de07845e7a0b	2026-04-10 14:14:51.088994	2026-05-10 14:14:51.089131	\N	2026-04-11 07:08:52.348681	\N	f943112a-fbcb-46c8-bfc3-877ecfcec2f7
193	1	UEFJluMdUUUKoCibVUscu0BRP7IPA9pv+DwIdFI/4YY=	95364d5b-5456-403e-993b-c5b99e732e66	2026-04-10 17:23:37.001615	2026-05-10 17:23:37.001691	\N	2026-04-11 12:28:07.809743	\N	aeb9b673-6d58-4840-824f-e881f41cae16
198	1	Kf2rQw0wSHx6/DmpZfGusIUpxlX3f1Pm2M1KdX6x7Ls=	818fee56-8c97-43f4-8f12-ff6075fa3dfe	2026-04-11 12:11:13.56862	2026-05-11 12:11:13.568781	\N	2026-04-12 18:22:23.663483	\N	1492e6c1-6be0-473f-b573-19891791e6df
189	1	ng8C075Vq5Lt25W+3HqTkJfoKrnTisQdjJC9C+RltPs=	1aee2e09-dbd6-4185-ad98-6ca0b5dcfa9d	2026-04-10 14:42:14.281598	2026-05-10 14:42:14.28168	2026-04-10 14:59:15.347112	2026-04-11 11:45:54.067829	190	2e5e59a7-780f-4389-9755-b237fc123d58
190	1	gypE8oSL19ezSqMbpWdhs8xEnVtnh4IAkI4iLTWlQpU=	b4cd20e5-23ee-4147-8ebd-0bc812a1248b	2026-04-10 14:59:15.347772	2026-05-10 14:59:15.347885	\N	2026-04-11 11:45:54.067829	\N	2e5e59a7-780f-4389-9755-b237fc123d58
194	1	jCL9cZmKco8nvKP+CMyhTnpqLESxh9YCPXv5siuh/H0=	630e5ba3-0c0a-4e96-81ba-72bb386c9faa	2026-04-10 17:41:02.508121	2026-05-10 17:41:02.50821	\N	2026-04-11 14:46:57.95933	\N	e080b4b1-7e0f-4c71-9322-6132b924a40f
195	1	dHpv2fqoqKoHDLHlRdf+avFkmv7ueX1XhBP0V5NS5G8=	18da6197-d45e-48b3-867d-c3b4ce1d9987	2026-04-11 07:08:52.712969	2026-05-11 07:08:52.713238	2026-04-11 07:30:37.972827	2026-04-11 15:04:58.725951	196	9427633c-90d3-4ab1-863a-e55150912737
196	1	i4dQ9FAK5SJEh2sHAU1rbB1DEcOBWkZTnGXOx8KRicw=	85fee726-5f7c-4990-9572-efe0f5ff6c73	2026-04-11 07:30:37.973719	2026-05-11 07:30:37.973972	\N	2026-04-11 15:04:58.725951	\N	9427633c-90d3-4ab1-863a-e55150912737
197	1	P535h9zEypXiPK6F49WjrWaroG+f0eCtzLFyog2PK+0=	8b4fe0e8-d2be-4aae-9206-6d30df0af0aa	2026-04-11 11:45:54.569341	2026-05-11 11:45:54.569565	\N	2026-04-12 12:16:55.55525	\N	6ce94f84-080d-4e28-8791-64f93fe2ab43
362	7	scyZYH+uIgNtU9AaPNyYj0GW8GOvfs1302jtuaK0wBw=	f0c68b48-f219-4e70-8090-4eee7fe59f67	2026-04-23 17:53:17.135944	2026-05-23 17:53:17.135947	\N	2026-04-24 10:50:59.549621	\N	11c8cf1c-c721-4c24-9d6b-2d81b3bc8408
199	1	Dxg4fWShk0G7ncDcX048gIl71FrkYaAfVchBPE4ec4U=	70aac621-9791-479d-8b53-4b900a463fdb	2026-04-11 12:28:08.251515	2026-05-11 12:28:08.251635	\N	2026-04-13 19:15:22.565729	\N	f86b475c-f30b-46b6-9ad4-a890d99c7b6b
206	1	GEqthyGGbVWCeTx1fJ/6qm7V0fez/1ks2BjR/se7GY8=	e4e9d9ef-f03a-4a1e-be47-d32c22f065b4	2026-04-13 19:15:22.922363	2026-05-13 19:15:22.922484	2026-04-13 19:45:03.70975	\N	208	478c702a-4ebf-40da-84a5-bd350b79387d
208	1	PJgKgWSWJpQOuxsNa+PQJGF8/s/b/w9HQPC4kIh7BGU=	5baffb0d-d5f1-474e-82b4-19ddcddbd627	2026-04-13 19:45:03.710459	2026-05-13 19:45:03.710638	2026-04-13 21:53:44.873828	\N	210	478c702a-4ebf-40da-84a5-bd350b79387d
200	1	jr5YFZIHAhWtux7T2ZizjDRw4gdSYOFpMavMyemPwNM=	7cc1e48f-d71f-42e3-970b-6c8cd264f106	2026-04-11 14:46:58.517257	2026-05-11 14:46:58.517387	\N	2026-04-13 22:02:36.650388	\N	87cbff08-9cd6-4796-8a12-e021ea288c36
352	1	Hbxp7IcOTbtnAfiVw31a3Jn7dEepq/gmsPpEUu1TWn0=	d4a8e9f4-150c-4d0a-90b0-e81107a79b0e	2026-04-23 14:09:52.854924	2026-05-23 14:09:52.855349	2026-04-23 14:13:20.669334	2026-04-23 17:24:29.709188	353	99e79b95-3389-4768-b970-dd8e7e7b8265
201	1	zqUyLQN5lAm9xJvom8ROQFVl1GMc4vo7kOiQ/Aw7dsQ=	24f3c415-af71-48bb-9257-b76330c3e6eb	2026-04-11 15:04:59.266543	2026-05-11 15:04:59.266798	\N	2026-04-13 22:02:36.650388	\N	906a5dde-9e3a-47f4-9127-2f87218ef714
204	1	eeZLOyE3BLxqYRaCv3BnN5day5DhUGDHoaTMrmxycIA=	1d0fd35c-1c7e-4f58-a939-3b76bf734f20	2026-04-12 12:37:43.187343	2026-05-12 12:37:43.187535	\N	2026-04-13 22:02:36.650388	\N	9d2cfa35-90da-4102-bc5f-57ea8d69b94c
374	7	ATFtryItQmO/6cgo4cYceVm7HDXktjnfLCAQ66Q+x8Y=	466d06c0-9a81-4f75-8fa5-b6ec96dc21b8	2026-04-24 13:42:21.80586	2026-05-24 13:42:21.805951	2026-04-24 13:57:22.533579	\N	375	0a0a76c4-576e-4558-a253-96cb5a168d30
207	7	UgjWgkk8A0lTpwHKRryLJPQ2dTrBbPg+2qq9tRSxl2M=	b2f6a319-be31-4733-ba29-ac267292ba29	2026-04-13 19:16:18.751678	2026-05-13 19:16:18.751679	2026-04-13 19:45:50.993969	2026-04-24 10:00:45.627874	209	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
202	7	rKRAUQ865uPN9CIstIQuWNaRGUAdZ5UGCZ3MTZh4aJE=	e84d6542-7843-422f-8488-650a5e8d3221	2026-04-11 15:05:57.231379	2026-05-11 15:05:57.231387	2026-04-13 19:16:18.751273	2026-04-24 10:00:45.627874	207	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
209	7	rIrx7vCb3BfPfmOZnRNY0g3YWUdwZOk9ITja9T5chqQ=	c307b752-ef29-4593-9669-403de2b0e16d	2026-04-13 19:45:50.993994	2026-05-13 19:45:50.993994	2026-04-13 21:53:55.102058	2026-04-24 10:00:45.627874	211	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
383	7	L0UtO7mtNPMaWmoYkmF/8eObUptu1pxl0uLmcLzehy8=	1cb66307-bb74-472e-acd0-ec0dfa6ee9b8	2026-04-24 21:56:49.554151	2026-05-24 21:56:49.554317	2026-04-24 22:19:53.814238	\N	384	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
391	7	hV/a2kaAOVjrRPX5EBo4AUPxIelaaBU17fXVOtZOZvk=	00b65652-cc1d-4435-9b9c-ae31c7d6871c	2026-04-25 13:15:43.261595	2026-05-25 13:15:43.261641	2026-04-25 13:37:06.173381	\N	392	59a2bff5-17bb-4c66-a065-32ff2ce900cc
205	1	YOiiqjne2eK62R6ieid+dgsI6ya5gmRqTDVx3uwWzNk=	d7f5304d-adc4-477d-9aed-f185c066c544	2026-04-12 18:22:24.069548	2026-05-12 18:22:24.069668	\N	2026-04-13 22:02:36.650388	\N	af0bfc3e-4a0b-485f-9671-0ff08e13564a
210	1	Rv7WwZ1qk2P6lfO1ISNg9ufj6Su4Bnp6Uk7Ogle3qcs=	64b32e2f-bed1-44ad-8391-fec27341bb9b	2026-04-13 21:53:44.875607	2026-05-13 21:53:44.875745	\N	2026-04-13 22:02:36.650388	\N	478c702a-4ebf-40da-84a5-bd350b79387d
215	1	9FUSAvLwK2+xBCcrKKjZ4RQY5in6KKhE/QNyQt+o9vo=	2004d48f-0dfe-4720-aa6f-cb5c47e40ff1	2026-04-14 10:50:52.757163	2026-05-14 10:50:52.757329	2026-04-14 11:07:11.02426	\N	216	1bee7d6d-c851-426c-802f-a5aa6b1def87
212	1	ignLymSoahPbQfQIp8xgtQY2DE3ish3qRvaYUdnyQ80=	88ab2a63-f0ac-40f4-ac79-57ecf692f3ae	2026-04-13 22:02:40.041249	2026-05-13 22:02:40.041322	\N	2026-04-15 07:03:12.932792	\N	22eb67a5-52e0-4c86-8bd6-be072d2e1aef
230	1	/oSUzmN+ayc4mzUNO9A8B8zexSY4LKWh9+R9T1Vkark=	af1e9d1d-43d3-4d99-994e-70ea9b20f248	2026-04-16 11:50:17.791708	2026-05-16 11:50:17.791802	\N	2026-04-17 17:01:47.822782	\N	ea9ba0f9-bc8d-4496-9a20-6a3674987104
218	1	nup79Ie7ART5oI9kSd5OEmswHXr9ugVYwV9Hv5Zp1ig=	7fbadd6a-257a-40f3-bfe3-3b5629885940	2026-04-15 07:03:13.343148	2026-05-15 07:03:13.343351	2026-04-15 07:18:39.085745	\N	220	acfb0bcf-5dd8-44be-ac2d-247a4100f95a
220	1	iflq8SJyVXmlcWjIVLg+Qd19yynmjpwf6OC1Wh0fT1k=	00ebc895-4242-46f5-909c-dce501368d17	2026-04-15 07:18:39.085757	2026-05-15 07:18:39.085757	2026-04-15 12:09:34.323518	\N	221	acfb0bcf-5dd8-44be-ac2d-247a4100f95a
363	7	aQYXNfoj0wJavHIUy6M0UKHJstrrNP9mllztvwq7ROo=	2a476480-8ddc-497a-bcb7-0060ad63e8d0	2026-04-23 18:11:15.157254	2026-05-23 18:11:15.157337	2026-04-24 07:14:40.792362	2026-04-24 11:24:24.665593	364	94fc452b-a2c4-4c84-8bd7-d1442b9381ce
213	1	dzY9YAiS//FPWAdNu54X99ZfUnxIT9PwesImChg9hz0=	8a0ba8f6-5861-4f20-b01b-3be192f3deec	2026-04-14 07:55:42.103734	2026-05-14 07:55:42.103906	\N	2026-04-15 13:31:11.607256	\N	4df124e3-96ba-48cc-aa0b-19febdfed713
353	1	wOxZQRpfy4rhaK66HoqpwJtG3TVSMAJcwecUacsCUkU=	c8b15f47-68e2-4b47-8ec9-9fcd00fbb6de	2026-04-23 14:13:20.670346	2026-05-23 14:13:20.6705	\N	2026-04-23 17:24:29.709188	\N	99e79b95-3389-4768-b970-dd8e7e7b8265
223	1	G5yN83ue2jt1CplTWdy0XbD6jn2EaaygMAx5YeXQV8s=	300d0f2e-9911-443f-ba7f-22f0cbc57815	2026-04-15 13:31:11.973117	2026-05-15 13:31:11.973226	2026-04-15 13:48:42.017068	\N	224	bcae7780-a37a-4736-997e-696109192355
224	1	B5idj5Z65O1jXeV8qK3cPINqHo5NYIIVbHXv4dLbP7o=	6b8b0fa8-8d5a-40af-a750-3101be5efdf0	2026-04-15 13:48:42.020906	2026-05-15 13:48:42.021031	2026-04-15 15:54:11.416798	\N	225	bcae7780-a37a-4736-997e-696109192355
225	1	tWsNHcaQFJ6y6ZUX7ETH0Bam4WrxT6Ay5+W59YvPU00=	3148cd06-f058-4975-ab25-dc1ce8ea69ea	2026-04-15 15:54:11.417366	2026-05-15 15:54:11.417446	2026-04-15 16:12:14.149368	\N	226	bcae7780-a37a-4736-997e-696109192355
214	1	8lih7M9JvEuT71zIRd7YtLPw8nyynl/YIDv8KWsihRk=	007ec655-23c3-4f86-81bb-999fcb042e9e	2026-04-14 10:35:08.689237	2026-05-14 10:35:08.689343	\N	2026-04-15 16:13:38.352008	\N	bb726ad3-ed8c-493a-8a78-4ca3a923d966
216	1	XOTV1SUSZc6P/rb9WgQCt/2fv6W+N+PLX6cX2rcV4Is=	1b757dba-674c-475f-a822-afc4c6c05a88	2026-04-14 11:07:11.028602	2026-05-14 11:07:11.028876	\N	2026-04-15 16:13:38.352008	\N	1bee7d6d-c851-426c-802f-a5aa6b1def87
217	1	Anb/Kd8eKEbTpaVBd+rfgZR1gg/CJ/Y8/vYwltZrZ6E=	85bb9c7e-520b-4979-9788-75b8ab622143	2026-04-14 14:11:22.91074	2026-05-14 14:11:22.910849	\N	2026-04-15 16:13:38.352008	\N	41faa801-9377-4ab3-930b-00cca2aed195
221	1	75u/is7YZ7AGSFTDEMrg5XMtnDFVQ++qcLi56Oe/uIg=	20b17e25-faf0-4e5f-8455-59507995e0c9	2026-04-15 12:09:34.324612	2026-05-15 12:09:34.324774	\N	2026-04-15 16:13:38.352008	\N	acfb0bcf-5dd8-44be-ac2d-247a4100f95a
226	1	2YoYmK63woSinV4wQkI8Vk81EqsInUeEWXhTsGnDHXU=	50777cfd-fce7-46df-9a18-e34edf0e4383	2026-04-15 16:12:14.15002	2026-05-15 16:12:14.150159	\N	2026-04-15 16:13:38.352008	\N	bcae7780-a37a-4736-997e-696109192355
211	7	8sPZ4lt6TDDkaqQfsuNlTTattJre6FqB6b0kRsM+Gw0=	1110a1f2-cce9-488b-a6b3-3488a51241e6	2026-04-13 21:53:55.102066	2026-05-13 21:53:55.102066	2026-04-15 07:16:45.630128	2026-04-24 10:00:45.627874	219	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
233	1	Mxu3je/hRw1R75p2dQvkPX9/O8OOw553tVxedz/YPkE=	4018e924-c8ca-46bf-9b43-c10b4e938c0e	2026-04-16 13:13:27.34303	2026-05-16 13:13:27.343141	\N	2026-04-22 16:05:07.390225	\N	9a1bf8da-02fb-4e2d-b7b2-7c2187db8c6a
219	7	GWt9Up2NNaVjcSbvcLmQjaKINLteqSBMoEGIFqIoTb4=	cdea126b-41a6-47b7-a680-8875c5f7ce92	2026-04-15 07:16:45.631077	2026-05-15 07:16:45.63126	2026-04-15 12:09:55.424909	2026-04-24 10:00:45.627874	222	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
236	1	CINiGgW5JyArP7I8VsRzWhT+2s9uNIVG0B1EteFsC+U=	208fce17-6cbe-48ae-8c01-ba9c0c8d8358	2026-04-17 11:14:47.535431	2026-05-17 11:14:47.535555	\N	2026-04-22 20:00:01.656884	\N	0b2fc400-36db-42c8-b1e5-5b25f5095d86
227	1	i69vPnzpkg+6eNorJgjM4J6RFSZfC3FPt7DNVZBzx/Q=	8d06d9d2-915f-4139-b9ec-b6e8ce3365ef	2026-04-15 16:13:41.923779	2026-05-15 16:13:41.923781	2026-04-15 16:28:49.525585	2026-04-17 11:14:47.147709	228	ede28c00-f194-4394-a7c3-91c27d38fb7e
228	1	4al9mQ0f7owo73R3C8UZ9jYrGOLG6JZfhGDOT4bPztE=	8be8cebe-d12a-48aa-bfc1-65a5a1580613	2026-04-15 16:28:49.526654	2026-05-15 16:28:49.526817	2026-04-15 19:31:09.165905	2026-04-17 11:14:47.147709	229	ede28c00-f194-4394-a7c3-91c27d38fb7e
229	1	lTaVRLYluZcutdNusoIDuQete+dvQrAA3lbxcs5D5zc=	40944633-f692-4cf0-937d-df168ff066f8	2026-04-15 19:31:09.166569	2026-05-15 19:31:09.166707	\N	2026-04-17 11:14:47.147709	\N	ede28c00-f194-4394-a7c3-91c27d38fb7e
222	7	NAKgGgvlbUJnANyc6rIQVMQuSefHs8MKdIX37tkuNEU=	c89a1c39-00d3-4fdb-be3a-ebe5d943314a	2026-04-15 12:09:55.424922	2026-05-15 12:09:55.424922	2026-04-18 14:59:17.845917	2026-04-24 10:00:45.627874	241	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
231	1	C6dLByCJMNaJqslR0EkGhKvKTJBGrEJhCt0PkLNTzZk=	5ee23cb2-69e6-4db6-a450-e23e1d65f237	2026-04-16 12:21:00.519128	2026-05-16 12:21:00.519259	2026-04-16 12:55:04.495886	2026-04-22 15:47:46.615831	232	1e9eb90d-de0c-43e1-bb34-077dc9dab72a
241	7	TgheKqrmpiM0TGIuEUH2cm1oAL/BH8M3XItTDkEaZJo=	8c8c48ef-0853-4fe6-93ff-807b93445f50	2026-04-18 14:59:17.845998	2026-05-18 14:59:17.845999	2026-04-18 15:18:29.542672	2026-04-24 10:00:45.627874	243	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
243	7	7EO4LoyJUMovGdMVLr3VyVDv+4Q+EBR7HIgsUjqJsWc=	adc49f70-d86b-41d0-9f80-3b7a7beaf5c4	2026-04-18 15:18:29.542679	2026-05-18 15:18:29.542679	2026-04-18 15:39:50.348358	2026-04-24 10:00:45.627874	245	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
234	1	C/FWIRkxtEr0wZz4DfNmAk/MCR7qwNibs7Nns7iCt7o=	33f69882-58a4-474f-8374-1a179adab067	2026-04-16 22:42:25.498074	2026-05-16 22:42:25.498244	2026-04-16 23:41:17.652321	2026-04-22 18:28:24.655213	235	1c33ec3e-c723-4d2f-9cf5-ed7637c8acde
235	1	cK9ZvIxO8QJg4/AEnWUR7r9UL0g3mLJnnSZHYUQxHSU=	9f205a65-1e4d-4ba5-9c30-f336969c9c00	2026-04-16 23:41:17.652936	2026-05-16 23:41:17.653012	\N	2026-04-22 18:28:24.655213	\N	1c33ec3e-c723-4d2f-9cf5-ed7637c8acde
239	1	Gad+g+VRNtAyVg1RTqxVWaXFcc6gnVMGiEqcF1shePo=	3dc495e5-a74b-45db-8837-008de53bfaff	2026-04-17 19:06:03.052065	2026-05-17 19:06:03.05216	2026-04-18 14:58:39.113066	2026-04-23 12:31:54.462736	240	e5a39333-f108-45d7-964b-4e173d0b809c
354	1	XS8F2K0FeuG7MU3uITJqTQAnshBgScFPAAbPvRqmD5c=	587e0cef-3a23-41fb-a171-2e6a24a066ec	2026-04-23 14:53:16.073869	2026-05-23 14:53:16.073953	\N	2026-04-23 17:52:46.757531	\N	7480edee-6ca7-4598-98af-d23a65babc93
277	21	ysjp0b8Kl0Bd9/BGX4/vfw9Wer7H+QMAYLA1BJ9aAxY=	55b499fb-a4e8-4809-8feb-d7c5af0227c1	2026-04-19 12:36:16.356553	2026-05-19 12:36:16.356553	2026-04-19 12:53:42.859147	\N	279	5bca52ac-0b48-4592-b795-10b0aa545484
245	7	aUbpdeSlfusbnI8ywhLQmLzj9CBURkP11V1pMlxyLNA=	bc842f31-3793-4462-9fd5-346c435ecfa4	2026-04-18 15:39:50.34837	2026-05-18 15:39:50.34837	2026-04-18 15:56:12.485287	2026-04-24 10:00:45.627874	247	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
247	7	uWQiLaCcLm7QxqCTbkWDqY6YIw5ueASJPMr35ABaQe0=	5416406e-bf77-4382-9895-4b5075f1df9a	2026-04-18 15:56:12.485296	2026-05-18 15:56:12.485296	2026-04-18 16:11:14.018965	2026-04-24 10:00:45.627874	248	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
248	7	yIXep59iCn4+P1w1qVpqwlvhYXwiqOHHcWxmjY19P3w=	4784cfe4-598f-4198-bb6b-2d45dec4a7a9	2026-04-18 16:11:14.018982	2026-05-18 16:11:14.018982	2026-04-18 18:06:01.236926	2026-04-24 10:00:45.627874	256	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
255	21	YybO8+h8bicebWzngPmtu71EWbXHFdfP0o8Bf43zs98=	2a33c14f-82b6-4327-9842-417526386a10	2026-04-18 18:02:37.287513	2026-05-18 18:02:37.287575	2026-04-19 08:01:56.804931	\N	264	5bca52ac-0b48-4592-b795-10b0aa545484
261	7	ogCV1QEOUwfD+Mt074rz+ngv/rOMJJdZ74mmIdPHWl8=	85a61995-23cc-452d-89dc-83387cd0cf23	2026-04-19 07:37:42.795145	2026-05-19 07:37:42.795146	2026-04-19 07:56:16.064836	2026-04-24 10:00:45.627874	263	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
256	7	BIAE/enNKPEzr3OgMk178Nwe/y0LYkTKCE4KGb7RX8M=	a4353e1b-a08f-4d8d-9adb-070334d968fd	2026-04-18 18:06:01.237118	2026-05-18 18:06:01.237119	2026-04-19 07:02:07.011763	2026-04-24 10:00:45.627874	258	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
258	7	tP3XcGLXS6qCPL60vzWy89sK0esk+Vf5Ts8N1pbD6QY=	7ed0bbf2-3a7b-4c4d-b541-8f5f3d129fb4	2026-04-19 07:02:07.011789	2026-05-19 07:02:07.011789	2026-04-19 07:37:42.795129	2026-04-24 10:00:45.627874	261	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
250	21	gUI4P5RmB742Lc7ASV0WwHgWBqv58C9x1nXPaPIP4PY=	acc8c1d3-41a9-42cd-b5e8-af19544f8875	2026-04-18 16:59:32.518514	2026-05-18 16:59:32.51861	2026-04-18 18:02:37.286977	\N	255	5bca52ac-0b48-4592-b795-10b0aa545484
270	7	mbUsW3V+0LuxMw0ZDZFlFJnera0ogJT0SHc5txjbMDU=	545b2645-b1b9-406c-affe-f7490a71baad	2026-04-19 09:11:56.96705	2026-05-19 09:11:56.967179	\N	2026-04-24 10:00:45.627874	\N	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
263	7	fFH78Pd7sIW+fOjoDvC/y42iJLiB4yRCESTvOpIN5+Q=	a05c1705-bc90-407e-8c5b-e3c0b027fd80	2026-04-19 07:56:16.065876	2026-05-19 07:56:16.066166	2026-04-19 09:11:56.965983	2026-04-24 10:00:45.627874	270	e3363d9b-1221-4df6-b2ad-9e7a01bc1fcb
364	7	8yju2fciGlAhQSsbdrFCKrm3Co4hERk4sGm8fBvu0Jw=	9a56524e-b5ca-4198-bb38-d676ab779121	2026-04-24 07:14:40.793768	2026-05-24 07:14:40.793905	\N	2026-04-24 11:24:24.665593	\N	94fc452b-a2c4-4c84-8bd7-d1442b9381ce
392	7	7Diu1oA5sAquf84xt5uraOJH57kSsJRY431ZGSSBMvQ=	412453c2-ea7e-4580-9501-d1d5409ecc2b	2026-04-25 13:37:06.17382	2026-05-25 13:37:06.173883	\N	2026-04-25 16:23:34.680333	\N	59a2bff5-17bb-4c66-a065-32ff2ce900cc
366	7	FcwFWIw3j83FmC5oMqe33Sz60R8NHweImt+tOF2vhl8=	7bd05e37-8a2b-4f83-be72-8c033779f8a4	2026-04-24 10:00:46.147243	2026-05-24 10:00:46.147569	\N	2026-04-24 14:25:20.014759	\N	a7adb146-61a0-4c6b-800f-3b29baf59155
384	7	/9R+YMYxFS65XBuY++WlLdCFhBe6HL+deB0cwLazCFQ=	9d7e5408-471a-43f2-aaff-d20c1d962034	2026-04-24 22:19:53.814892	2026-05-24 22:19:53.814993	2026-04-25 07:31:41.860144	\N	385	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
375	7	3dp/D7OiT16OHjjovyMT8e0cx8teHXr0zXXVZPjIq08=	12e940e1-8141-4198-b22a-13fde010f74b	2026-04-24 13:57:22.534128	2026-05-24 13:57:22.534209	\N	2026-04-25 10:48:04.501053	\N	0a0a76c4-576e-4558-a253-96cb5a168d30
415	1	mFjBvxvP5Q6aishuqd9HbUYe3sgWgr/UcAoblOgQKHk=	25a7b2e7-30f7-4115-89ea-38d81fad0613	2026-04-27 03:08:19.165905	2026-05-27 03:08:19.166015	\N	2026-04-28 08:58:40.082306	\N	e25dfc46-e58e-4bf8-8a1b-634bab55c57b
398	1	/CdpaeBRTW2SyupHcma0EQDPFla6dblIycG+2CQ7kqk=	25fc0e15-5d21-4660-abfb-b6bc1f4521bd	2026-04-25 16:23:38.567928	2026-05-25 16:23:38.568057	2026-04-25 19:29:46.01425	2026-04-26 18:03:02.326391	399	a2f1b67b-b33f-4851-9207-745e8365a08d
264	21	zTJ0MyLksmoS1yCmYUcVZkHPPawus//zCDqzI9mS5Dk=	bcb750b4-b8cd-445c-80bc-407fea74f5f5	2026-04-19 08:01:56.804949	2026-05-19 08:01:56.804949	2026-04-19 08:48:22.6124	\N	267	5bca52ac-0b48-4592-b795-10b0aa545484
269	21	+tC4qbqcZJ4XU58u2kZLtpDYqBGEQg7HXLScapkuun8=	2ef4e90c-87fd-4c9c-9581-494abe9a1651	2026-04-19 09:03:07.105792	2026-05-19 09:03:07.105792	2026-04-19 09:21:04.922536	\N	272	5bca52ac-0b48-4592-b795-10b0aa545484
407	1	2WXDmWGEE2eGiKDPTDxLimNN6qY3NfIH6KEu5FOQCbg=	e32c39b4-265f-416b-8684-bf2edadc2db4	2026-04-26 13:03:17.696467	2026-05-26 13:03:17.696582	2026-04-26 14:32:13.25012	2026-04-27 03:08:18.904544	408	515d4f3d-f44c-4dc5-9570-1891b51a79c4
406	1	DEyow1Ao62tPmUb4+Zix+PXJduZi5BwsTUlpN8f96xI=	8698dc6d-e259-45b6-b189-591fbbc90b7c	2026-04-26 12:46:31.191177	2026-05-26 12:46:31.191264	2026-04-26 13:03:17.695866	2026-04-27 03:08:18.904544	407	515d4f3d-f44c-4dc5-9570-1891b51a79c4
267	21	M4dJbb8eJ3Rbc/Hdcy18YSfOrXbLEvgKtxJvv0BrCwc=	9d514031-a7ae-46d8-b24a-d2d942927a0b	2026-04-19 08:48:22.612412	2026-05-19 08:48:22.612412	2026-04-19 09:03:07.105782	\N	269	5bca52ac-0b48-4592-b795-10b0aa545484
429	1	IAkFmbYm5eEmVlBB3akdPh8jdghMHHiE3bMqBym4HaM=	b4aeaf96-c49d-4c4a-a4ce-b91cd0867a06	2026-04-29 15:11:22.442474	2026-05-29 15:11:22.442536	2026-04-29 15:27:27.331903	\N	430	1ec4f6f6-b212-471a-9704-f8689e11e9b7
430	1	RltfK+f9OrPKc3wZWo1sQ7B2iWtbbK2u2Ibs79YRiTo=	a0241565-26c3-4edf-a05d-e86a31cb07ec	2026-04-29 15:27:27.33244	2026-05-29 15:27:27.332514	2026-04-29 15:42:02.424202	\N	431	1ec4f6f6-b212-471a-9704-f8689e11e9b7
279	21	LsaKA5DrMJDLZPyLs3ICGBlIVWmQ7kc5yCfAZ2T7ux0=	66718598-6f5f-4256-8eaa-b9e31a46caf9	2026-04-19 12:53:42.859166	2026-05-19 12:53:42.859166	2026-04-19 13:56:19.158028	\N	281	5bca52ac-0b48-4592-b795-10b0aa545484
436	1	b9DPPfpfOPSjKdoD8UC123anRoD6esZI19Pr4Hl+pXY=	0cfe5fc9-7062-40ec-9e53-d9f03d62cbeb	2026-04-30 09:40:30.320601	2026-05-30 09:40:30.320742	2026-04-30 09:55:07.680023	\N	437	1ec4f6f6-b212-471a-9704-f8689e11e9b7
437	1	RpJ42Kzi8UpM2IJ87T0l8LssVpLqjF3aRSAiHLBLmUQ=	cf34ce50-0545-436c-a80b-c4127e2a44e5	2026-04-30 09:55:07.680569	2026-05-30 09:55:07.680648	2026-04-30 10:12:33.320131	\N	438	1ec4f6f6-b212-471a-9704-f8689e11e9b7
442	1	ukBAgnJVAeAyFORJVxVqqjJP8WqdojTnGdJkJDREnP8=	ca426e69-99c3-4ec7-9371-e31a3b390222	2026-04-30 11:35:30.473198	2026-05-30 11:35:30.47331	2026-04-30 12:24:22.495523	\N	443	1ec4f6f6-b212-471a-9704-f8689e11e9b7
272	21	RS6n0RLUHBAyDAKS0a8kleA1mqhTryjilG7/BBU/E3U=	f5b96c8f-f43b-4bce-b49d-802f7b4a6455	2026-04-19 09:21:04.922548	2026-05-19 09:21:04.922548	2026-04-19 12:36:16.356509	\N	277	5bca52ac-0b48-4592-b795-10b0aa545484
447	1	mEun+6IczW82fkC0lC6UFLPdeOtt7h9+XVXmIVrIH3w=	59c986a2-fd50-48aa-92e4-34d6738f06b4	2026-04-30 13:52:45.921679	2026-05-30 13:52:45.92179	2026-04-30 14:09:36.302697	\N	448	1ec4f6f6-b212-471a-9704-f8689e11e9b7
281	21	eaMWITDeJTYaYzDNNZqWtONfmhLpQsw0g5WlYl326so=	5cbff247-6c84-41be-b4c2-f291103079a0	2026-04-19 13:56:19.158041	2026-05-19 13:56:19.158041	2026-04-19 14:13:36.744602	\N	283	5bca52ac-0b48-4592-b795-10b0aa545484
452	1	Kajl0/I9qDGjeb2erdDq51Tg+O34CVE301EoNmfBL50=	aa2e03f8-4ec4-4b6d-9892-23415c53a951	2026-04-30 17:20:17.348828	2026-05-30 17:20:17.348893	2026-04-30 18:16:35.383065	\N	453	1ec4f6f6-b212-471a-9704-f8689e11e9b7
458	7	vWcfvg4sf7lHdAqWefIpIo4Rd4sIZtJcApkFHPLZLTw=	0083e4a4-2e79-4ba5-8960-3f68d5d70370	2026-04-30 19:45:26.339058	2026-05-30 19:45:26.339193	\N	2026-04-30 19:48:42.953271	\N	a60fee0e-caa1-4f35-a1b6-c5df8643cffc
249	1	/zyUmzLteW7DrsTwY/cCbNPRw/qZVUjCvld1Ta3hDy0=	74d35721-9f51-4834-b428-5c45577851a9	2026-04-18 16:48:19.061458	2026-05-18 16:48:19.061561	2026-04-18 17:02:57.320283	2026-04-23 12:31:54.462736	251	e5a39333-f108-45d7-964b-4e173d0b809c
283	21	7D7CJDbCG36N2EoKZRaXmXOZNe5aRsvub6OZwPqtgTw=	952bf536-7e34-4c8a-8300-2998b46628a8	2026-04-19 14:13:36.744613	2026-05-19 14:13:36.744613	2026-04-19 15:58:53.84923	\N	285	5bca52ac-0b48-4592-b795-10b0aa545484
285	21	SGk99ta/F/5bBCTIxtIHiMHxdA79rOd68cMiy5x16IE=	3b71eaeb-ccb9-4b04-a08b-5c890037e8e6	2026-04-19 15:58:53.850009	2026-05-19 15:58:53.850113	2026-04-19 16:15:13.07658	\N	287	5bca52ac-0b48-4592-b795-10b0aa545484
465	1	Zr+IjypBLB6ptm0KiEj/TqHdz7Gcw6T9REZO4fLKv+Y=	0a6e6b54-d69a-4508-95f9-1e4d16e9f64d	2026-05-01 16:53:01.039955	2026-05-31 16:53:01.039994	2026-05-01 17:12:05.999256	\N	466	04dfdda0-3bd5-48d0-9600-8100cd554987
287	21	sbPqNIxWbA8tvzUL9dr+dCm0VTxXPxztU3hQqryG62M=	44fb3c5d-c8a4-4c8d-aa0c-aa908f56f081	2026-04-19 16:15:13.076593	2026-05-19 16:15:13.076593	2026-04-19 16:31:15.706923	\N	289	5bca52ac-0b48-4592-b795-10b0aa545484
355	1	ZKtCJaBppoRkSmVp8E5bKCyKVvFW10FEi765s0PxG3E=	b8cf3be7-9e3a-40e9-98fb-87692121e386	2026-04-23 15:38:34.859309	2026-05-23 15:38:34.859452	2026-04-23 15:53:11.86477	\N	356	cd103ea6-ece3-4299-a741-99c5f08a4bef
324	7	yCh87yStJwEN5fU1k7BfM+HcyrhuikwxQ+CBueHFIWo=	d009eafd-3985-4a84-b8b4-2e7d1b84728d	2026-04-22 13:12:29.313072	2026-05-22 13:12:29.313074	\N	2026-04-24 10:16:12.27832	\N	7c734d5a-c855-42b3-81c2-aa4bbf79fffd
423	1	dZklFQFKWwcGs3mmmOw2i5cFN/jbVvppWaTX6Q/PPUU=	8a055cb3-cbaf-4b06-b0ef-9ad594eb30b6	2026-04-29 07:54:52.275687	2026-05-29 07:54:52.275737	\N	2026-04-29 07:54:57.785602	\N	fd0bc5a9-8018-4b1d-8ea3-caebd1b07018
365	7	EwmK+/0rsK5NdMCO8C/20Tl5Nxe2AhMmPdJOamWk3S4=	1e23e148-4581-4514-9366-e637bc21b5ea	2026-04-24 08:03:16.703087	2026-05-24 08:03:16.703203	\N	2026-04-24 13:07:43.193955	\N	894e1d35-56ce-49d5-a4dd-1b9f77f3b33b
289	21	ad+EEiKYCjg+emK1IfnxorGtzgP9SpYYCZd/tKd6auY=	e1922336-7b58-467b-bc50-c2d0b3730447	2026-04-19 16:31:15.706933	2026-05-19 16:31:15.706933	2026-04-19 17:05:39.790681	\N	292	5bca52ac-0b48-4592-b795-10b0aa545484
367	7	AAOYwzGt5coUnAOSpZ1uuRVi3XZ5/Unjn42pGMXsVPc=	d98e7b3d-3713-4340-809d-21c6ac4bcba7	2026-04-24 10:16:12.750394	2026-05-24 10:16:12.750456	2026-04-24 10:32:02.915758	2026-04-24 14:42:37.414458	368	6b863808-48e9-4f57-a4c6-d35bffe12be4
419	1	VVkAzPgOk2IHrcvfvlSxCEd18aGjxStsYBbKvd5+Guo=	45c46351-37b5-46d9-b0ed-55bc071bad9a	2026-04-28 13:45:02.975938	2026-05-28 13:45:02.976004	2026-04-28 14:22:04.128401	2026-04-29 08:20:56.418468	420	fd0bc5a9-8018-4b1d-8ea3-caebd1b07018
385	7	/CMpMhtt6dXg4gPav0W+BzU53e+X7eo2wIPetYB6N14=	a9ebf75c-a664-4421-bad2-88cd73768594	2026-04-25 07:31:41.860524	2026-05-25 07:31:41.860578	2026-04-25 07:50:02.186231	\N	386	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
376	7	yDJl9pDoXNccHD9g/mdE71TT6cQcfzbj19dWl/TFm48=	7cd1a0b8-e8df-46b3-9156-feb4ddf7d120	2026-04-24 14:25:20.369575	2026-05-24 14:25:20.369887	\N	2026-04-25 10:48:04.501053	\N	040a3df6-12c6-463c-8ecd-241fadec9926
393	7	J8eGNm6uq9Z5jtpyccVD3WaiEZq1TwolIDbrf63gWPI=	8d0c0a87-0f57-457c-be07-7c9c1d462e35	2026-04-25 14:12:25.673085	2026-05-25 14:12:25.673156	2026-04-25 15:12:21.514914	\N	394	7caf6781-3311-4588-ba24-0e41947a2584
292	21	7Z5WQXMf+EGAXEXxjzOYHLiQRxzqe14IVqhGmVvxfB0=	787ca54e-4c79-404b-840c-98c7f9231228	2026-04-19 17:05:39.791467	2026-05-19 17:05:39.791623	2026-04-19 18:52:18.134593	\N	296	5bca52ac-0b48-4592-b795-10b0aa545484
431	1	1iO0EdvArkLcnE8Id5FJa+bqsQTRXvaHqgw+ZnhC0/o=	25081e13-5a66-456e-b0f5-69e58392aada	2026-04-29 15:42:02.424701	2026-05-29 15:42:02.424775	2026-04-29 15:57:07.959877	\N	432	1ec4f6f6-b212-471a-9704-f8689e11e9b7
438	1	7VtV4sxMw1UA0z6Y2uWgCKJSyihEwE1dBzhIMQ8KHs4=	aa9ad885-f5d6-40ce-b968-8b93db10193b	2026-04-30 10:12:33.32104	2026-05-30 10:12:33.32119	2026-04-30 10:44:17.026038	\N	439	1ec4f6f6-b212-471a-9704-f8689e11e9b7
400	1	Nt7BVda7UqhDoNJyj8A/WwxVJWHCsRoreht4WqtxNPY=	7fc053a7-3677-4550-bf63-49393c241c49	2026-04-25 20:06:59.630739	2026-05-25 20:06:59.630808	2026-04-26 10:40:17.582567	2026-04-26 18:19:48.779828	401	9f5e66c3-1be2-45a2-a501-edd0437c09f5
408	1	8/diNthbe04XtMF7tsp+njP5wMtLZQcnlfZPTCJBO+g=	1d552a03-1b9b-4e5e-82f5-698864ebf95c	2026-04-26 14:32:13.250702	2026-05-26 14:32:13.250756	2026-04-26 16:08:20.553478	2026-04-27 03:08:18.904544	409	515d4f3d-f44c-4dc5-9570-1891b51a79c4
416	1	GoD7n02Ua5h31+kucnVR/1fgghJM33FdLpbf1kgRBCI=	7350ce2a-f6dc-4662-b621-004ad9bab2e3	2026-04-27 03:23:10.912403	2026-05-27 03:23:10.912473	\N	2026-04-28 08:58:40.082306	\N	4c0627e0-484f-49af-8464-532def5433a6
418	1	nbBZchgNlDue0l4ipMZ/Gik3OpcFPb5mtJNMj4qvuco=	c53bcc8a-a3af-4378-8aed-09e1388e627f	2026-04-28 08:52:33.806985	2026-05-28 08:52:33.8071	\N	2026-04-28 08:58:40.082306	\N	a304b716-232e-4592-8a31-498112652a28
443	1	kO7sBkUyUmHBYvqFdfMHnHLSejkyE/gzDJDkVthL6ac=	539be8a9-c072-4c53-93ef-f14b8c5ff600	2026-04-30 12:24:22.496035	2026-05-30 12:24:22.496086	2026-04-30 12:40:27.458279	\N	444	1ec4f6f6-b212-471a-9704-f8689e11e9b7
448	1	Sci6ILmLuoIHV+AgOBrBIJsAXRAUIlNuaVKRg0odakI=	037fa77d-0de7-43ba-820f-14475ea28663	2026-04-30 14:09:36.303291	2026-05-30 14:09:36.303411	2026-04-30 14:30:10.581652	\N	449	1ec4f6f6-b212-471a-9704-f8689e11e9b7
453	1	dgZMb+mnWFf2WRKpnDMnES6sODo4jYsF016sQBpfi6M=	d9616420-ef28-43cd-810e-f7acd77c4183	2026-04-30 18:16:35.384251	2026-05-30 18:16:35.384384	2026-04-30 19:24:54.765797	\N	454	1ec4f6f6-b212-471a-9704-f8689e11e9b7
459	1	gH/XJzSUAjBK526/IPZatWl3q3WqrTVGYl2GSlBpQyk=	4350b4d8-d8b3-4d0b-97c2-1666b8c17e1b	2026-04-30 19:49:00.371543	2026-05-30 19:49:00.371587	2026-05-01 15:10:20.015983	\N	460	f0a7d85b-f4a6-4bbc-8667-b4cbc89919f5
460	1	XcM5QWMMY3LQOHIp+iV8YWxwuqt7WMvQpcpxKWhUias=	4082eaf1-d307-423c-b6f7-44589c5f2956	2026-05-01 15:10:20.016362	2026-05-31 15:10:20.016411	\N	2026-05-01 15:11:01.530173	\N	f0a7d85b-f4a6-4bbc-8667-b4cbc89919f5
464	1	QeBJ6xQ/bK4YsDSku5kvaoV+rg76FiQvEFCiZdLB1OQ=	a789199d-dd4a-4dff-b894-2c0cf0dff004	2026-05-01 16:38:18.698649	2026-05-31 16:38:18.698717	2026-05-01 16:53:01.039635	\N	465	04dfdda0-3bd5-48d0-9600-8100cd554987
469	1	lol+nSlmUJGeJyDO5b+L6WDvDfbVny7JsG9Ae8wzsRw=	ae5b8f17-2458-40e0-b826-2c5380af5d21	2026-05-01 18:17:38.445316	2026-05-31 18:17:38.445353	2026-05-01 18:35:06.178203	\N	470	04dfdda0-3bd5-48d0-9600-8100cd554987
473	1	LPSwBGd2OGcwCv7eFaud4LwrmsmikpLu+kVz//YohOM=	435907da-94eb-4bda-9759-abdec529020f	2026-05-01 20:15:29.268836	2026-05-31 20:15:29.268888	2026-05-02 07:47:28.357482	\N	474	04dfdda0-3bd5-48d0-9600-8100cd554987
477	1	249B07WASbWARO/TanJMzIWTQsR3rOLAUHpnIOonVbQ=	b7045c61-67e9-4647-8c22-8274e5c83d45	2026-05-02 09:57:09.023634	2026-06-01 09:57:09.023815	2026-05-02 10:24:01.895565	\N	478	04dfdda0-3bd5-48d0-9600-8100cd554987
481	1	hZSJt34TZ/OYIzl99oGlbcJpM14bGVfVC10dh3zGOIs=	b553e602-0bc9-4e69-9ede-ff8a9c24ef50	2026-05-02 11:51:10.882972	2026-06-01 11:51:10.883012	2026-05-02 12:31:04.529238	\N	482	04dfdda0-3bd5-48d0-9600-8100cd554987
485	1	wYlNQUaLePrvH3+jNn1bTmNWfFvfseKY8iJUU4ygcHk=	240ba7a0-44e8-4b87-9924-ed92c0845fba	2026-05-02 13:31:21.303449	2026-06-01 13:31:21.303502	\N	2026-05-02 13:40:43.582044	\N	04dfdda0-3bd5-48d0-9600-8100cd554987
489	1	mbytwKXGwP0wx5KlkM2ZM+8k7E0zYQIF/CNcqq1syLU=	8ebce010-7bfd-483b-9a59-46c330907c0d	2026-05-02 14:31:10.593083	2026-06-01 14:31:10.593124	2026-05-02 14:52:05.133389	\N	490	c60d868a-baa2-4585-be1a-7166d90e04dd
492	1	juD2v6Hwn4wOMr1/c6rzh18ORRNN3nquZWrrG6zVQHQ=	74d2031a-3180-40bd-a650-9397c3f2c2dd	2026-05-02 15:25:29.658915	2026-06-01 15:25:29.658953	2026-05-02 15:43:19.339317	\N	493	c60d868a-baa2-4585-be1a-7166d90e04dd
495	1	s1t2pdT7IqOWG4O5FeFJlGsa7kC6WJBv5o3klulIl+s=	da65c514-0cd0-4746-8bb0-8f1068f5390f	2026-05-02 18:58:25.299439	2026-06-01 18:58:25.299483	2026-05-02 19:14:04.547344	\N	496	8b40d916-37ae-44ac-b486-39436cf99e55
496	1	xYaJvtHtqHbApF81BR65kcn5za2+vkjdG76lswudHTI=	4e0fcb41-0e46-43cc-b82c-262b3acc2d79	2026-05-02 19:14:04.547662	2026-06-01 19:14:04.547714	2026-05-02 19:28:37.018739	\N	497	8b40d916-37ae-44ac-b486-39436cf99e55
498	1	ClsZYuN4ZAwFHLzMs8CqdnVy3ACvzdWQDsMiiuY0hzc=	b724dec0-3d69-4e0e-b5c9-6f1e07a3faa8	2026-05-02 20:04:10.206588	2026-06-01 20:04:10.206615	2026-05-03 03:14:59.616198	\N	499	8b40d916-37ae-44ac-b486-39436cf99e55
493	1	GYmkcNTcmSoI6teaeU2rB+4GkLyPL8U7z10FqjhFaZQ=	c2809cdb-6948-41f8-b409-7128d05b4461	2026-05-02 15:43:19.339702	2026-06-01 15:43:19.339774	\N	2026-05-08 15:34:04.164402	\N	c60d868a-baa2-4585-be1a-7166d90e04dd
307	1	0Gxtj2Br9ezY6XvOVgJ+wwfYLs56n1z0SUlkM6tRMCE=	8e431b51-9bc5-4d9f-a95b-8e52bac213af	2026-04-21 09:06:18.171718	2026-05-21 09:06:18.171841	2026-04-21 09:24:30.833095	2026-04-23 12:31:54.462736	308	e5a39333-f108-45d7-964b-4e173d0b809c
296	21	UBnpO6U2qQmResvZ947TG1Kn5or2UnbOgly5aAly56U=	62d71bac-4100-4d25-9916-bd4f6747a10b	2026-04-19 18:52:18.13526	2026-05-19 18:52:18.135346	2026-04-22 13:35:42.170176	\N	326	5bca52ac-0b48-4592-b795-10b0aa545484
338	1	FXKkeQ7Yszs6543QdFGL6hqKXZRsAL4jXhpTBhp9+4w=	f67b81ea-4cfa-4d4b-a32b-9d9945a72ff4	2026-04-22 19:22:31.759465	2026-05-22 19:22:31.759615	2026-04-22 19:38:27.989552	\N	340	3aefd8d6-501c-457c-af24-c8b34c683d6e
330	1	e/yBKhsOkivqXEPXvr0OKnXhQnCYJUSPP5Ziz+ZOODM=	209bf1ed-f13f-49a0-8506-04b4c03c4ba1	2026-04-22 15:47:47.023149	2026-05-22 15:47:47.023483	\N	2026-04-23 12:51:22.243051	\N	df2bf6ed-0aff-4468-9db5-d7fcdddce58e
326	21	n9MKjhJaZihJvxuDGWBqAZtBh0zRy4GMXLExSbk+bd4=	d6ad512a-3760-485f-9db4-48410bc1f861	2026-04-22 13:35:42.171126	2026-05-22 13:35:42.171223	2026-04-22 13:54:20.137227	\N	328	5bca52ac-0b48-4592-b795-10b0aa545484
325	1	1Tuc9DoaPzUNeAO4g1Vo6d+W0HHKZcFkRiBphgUwPvc=	960af9e1-8dc8-4022-a16b-3d5a60eccf82	2026-04-22 13:32:11.021451	2026-05-22 13:32:11.021537	2026-04-22 13:54:17.959567	2026-04-23 12:31:54.462736	327	e5a39333-f108-45d7-964b-4e173d0b809c
333	1	0nzyU9+D9825A5gGxGGUVT/gNsS/kH8UVbCtcXtCSnc=	246ed36d-cb97-4cc0-9551-ff399cfdde45	2026-04-22 16:43:55.314177	2026-05-22 16:43:55.314482	\N	2026-04-23 13:44:37.906473	\N	69468402-7fe2-4f9f-9e28-2684e46aba14
232	1	UoHVNDNrP3k5OVkUnaV3fbaNyNdjK/CB51icwbH/Md8=	ddd7019a-64cb-4d44-9d99-642de1f7fd9c	2026-04-16 12:55:04.49694	2026-05-16 12:55:04.497175	\N	2026-04-22 15:47:46.615831	\N	1e9eb90d-de0c-43e1-bb34-077dc9dab72a
328	21	P6655hvS/mFs9JNyXM1vXJsckZ9VOibVDQFAMgtls4s=	752abb93-1d0c-48ae-a76c-e2dc318863cc	2026-04-22 13:54:20.137237	2026-05-22 13:54:20.137237	2026-04-22 16:05:06.56288	\N	331	5bca52ac-0b48-4592-b795-10b0aa545484
341	1	/QEdBGy5d6M1yzY8AUbUsY64RF14hmeE2vsIGV2e2Es=	a6d6e98d-6b05-4e7e-bcaf-876ad565be7d	2026-04-22 20:00:02.025755	2026-05-22 20:00:02.02585	2026-04-22 20:31:43.998726	\N	342	cfc23950-b395-4cf6-846f-f4a4ffb67456
332	1	uYBcTDLwibtLps6RkE+UtDClnuJWGKcP4ta0/tEhrAM=	6013f719-b639-442a-bc35-c99ec72bd796	2026-04-22 16:05:07.794937	2026-05-22 16:05:07.794939	2026-04-22 16:43:55.31317	\N	333	69468402-7fe2-4f9f-9e28-2684e46aba14
329	1	UTv8qaOVfUid8i/HzRKN4tJsmxAvb8MuhuXzCBKivII=	48635ac3-bd10-44cf-a612-811ed6bbd4c0	2026-04-22 15:15:21.228721	2026-05-22 15:15:21.228896	\N	2026-04-23 12:31:54.462736	\N	e5a39333-f108-45d7-964b-4e173d0b809c
331	21	MTLgw3dDUjv88a1Cv8untdBOUnGn/OJ84ron8672zjQ=	ce09f274-1596-4319-926d-89d28601f64e	2026-04-22 16:05:06.563783	2026-05-22 16:05:06.563894	2026-04-22 16:47:56.156504	\N	334	5bca52ac-0b48-4592-b795-10b0aa545484
335	1	mHl6OEOdoqJpdlAtItgvGGtseMfy8wJTVjnVILUt0Qk=	996b68dc-a38e-4623-a4b5-ac156d3f156c	2026-04-22 18:28:25.027584	2026-05-22 18:28:25.027725	2026-04-22 18:50:31.093474	\N	336	3aefd8d6-501c-457c-af24-c8b34c683d6e
342	1	aNH7AyloxAArHZmqaLzIgmoH1bFfNXsPMH6M3lsg9lA=	57ae49a4-fa62-4ea3-ba9e-bc6da2a4b1c5	2026-04-22 20:31:43.999341	2026-05-22 20:31:43.999419	2026-04-23 08:03:48.601046	\N	343	cfc23950-b395-4cf6-846f-f4a4ffb67456
336	1	ZvZFIXgBlzA0jxUxiAqXxFCYJr+x94R32SW54LKY3KE=	a0461e39-ebe9-4d03-8b56-5405dd2be049	2026-04-22 18:50:31.094595	2026-05-22 18:50:31.09473	2026-04-22 19:05:57.218612	\N	337	3aefd8d6-501c-457c-af24-c8b34c683d6e
327	1	xSSrsy5UGYaVJS4EvEeP0XKBJ/XVFJeMkEH7krD+9iQ=	1462ae4b-1d4b-4dc8-bc2a-f53d64231407	2026-04-22 13:54:17.960076	2026-05-22 13:54:17.960143	2026-04-22 15:15:21.227778	2026-04-23 12:31:54.462736	329	e5a39333-f108-45d7-964b-4e173d0b809c
337	1	GFOEJ8GDKDaM71GMM3kPHRv3RSqO6ng613oCekAhTJ0=	8d084ee1-0113-4979-8aa1-1ccd11085085	2026-04-22 19:05:57.219445	2026-05-22 19:05:57.219552	2026-04-22 19:22:31.758909	\N	338	3aefd8d6-501c-457c-af24-c8b34c683d6e
339	21	GtwIBFP2vinvFOI9bkeeB50+ZQ/LLzy6b6G7N1F7pvs=	4eafb95d-cbab-4fce-b1f2-d429e1c0f75a	2026-04-22 19:26:11.192086	2026-05-22 19:26:11.192187	\N	\N	\N	5bca52ac-0b48-4592-b795-10b0aa545484
334	21	XoDr0qh5K/8EI8PmutGZ/pT+9OX1kcgqg9UdHqrKSuA=	b144e71b-d382-47dd-b8c9-6f4b53e702e4	2026-04-22 16:47:56.157279	2026-05-22 16:47:56.157423	2026-04-22 19:26:11.191251	\N	339	5bca52ac-0b48-4592-b795-10b0aa545484
340	1	qVRV/zvDXxgieCnPvziuqDZEjEVPInayHZprjK+OA6w=	1c7c532b-a230-40f9-b6dc-f0ec02e9acb8	2026-04-22 19:38:27.990553	2026-05-22 19:38:27.99071	\N	2026-04-23 13:44:37.906473	\N	3aefd8d6-501c-457c-af24-c8b34c683d6e
343	1	grDifgJ0EyoXvUCHEfkzEiL1EfPhXnnRspDIvR0stIs=	bcc219b1-2576-48d1-a94e-0a6ff745122b	2026-04-23 08:03:48.601897	2026-05-23 08:03:48.602071	2026-04-23 08:30:25.518903	\N	344	cfc23950-b395-4cf6-846f-f4a4ffb67456
346	1	Jo4sdsn+tzetXFewdt36undTDbyDsvNo5MQ8zuRmGJk=	aceccf23-9476-41e6-91ad-b8f5675e6cda	2026-04-23 11:35:41.974622	2026-05-23 11:35:41.974764	\N	2026-04-23 13:44:37.906473	\N	cfc23950-b395-4cf6-846f-f4a4ffb67456
344	1	/L2wG4glChPhLeoqOya+H1Hqet2vI0ftPQznJYm7OnQ=	bfd79dc7-d103-4ad6-8d1a-3c6e86dfc4dc	2026-04-23 08:30:25.519572	2026-05-23 08:30:25.51966	2026-04-23 11:18:24.910128	\N	345	cfc23950-b395-4cf6-846f-f4a4ffb67456
345	1	pPcagvQVwfdWn14rlmhVhA/616X60kXLMB5ra2wPHxE=	80ec9a67-8059-4aa3-8c05-61b2cd2f4137	2026-04-23 11:18:24.910825	2026-05-23 11:18:24.910923	2026-04-23 11:35:41.973817	\N	346	cfc23950-b395-4cf6-846f-f4a4ffb67456
237	1	Tl2SLB1FIA4aNlbRY66/qPPtNIS8sEkNEIi7hSnzIZo=	92a4a8d1-43dc-45fc-be9b-de6652700357	2026-04-17 17:01:48.231895	2026-05-17 17:01:48.232021	2026-04-17 18:50:13.326766	2026-04-23 12:31:54.462736	238	e5a39333-f108-45d7-964b-4e173d0b809c
240	1	F8o8DwAnQ8rAR+e1NDtBSpav2dEazKI/eUaKjjb+vQY=	33ec09fb-5ace-4ec0-891d-9dbd374b447e	2026-04-18 14:58:39.113707	2026-05-18 14:58:39.113875	2026-04-18 15:18:23.519152	2026-04-23 12:31:54.462736	242	e5a39333-f108-45d7-964b-4e173d0b809c
238	1	mGefVbkQ7X67d+Ur1+3pw/F6aLhoT0wnU/u6/UFROzI=	99511951-e049-4f61-acdd-4631d4b7167d	2026-04-17 18:50:13.327619	2026-05-17 18:50:13.327804	2026-04-17 19:06:03.051438	2026-04-23 12:31:54.462736	239	e5a39333-f108-45d7-964b-4e173d0b809c
242	1	MBJU64CulF1nQcNvfIpIWrcmbj8EXjXjV0r3jmHwc6M=	acd9d8f2-e8d9-4f26-ad0e-b3a2eb535bc5	2026-04-18 15:18:23.519846	2026-05-18 15:18:23.519929	2026-04-18 15:39:07.366591	2026-04-23 12:31:54.462736	244	e5a39333-f108-45d7-964b-4e173d0b809c
244	1	FYOvy3pENvO5Qg0L4DOQHwqTRhVPyRtSO9CRnmUS+GE=	4d6455cc-25f9-419e-8ca0-5d5eaee0b78a	2026-04-18 15:39:07.367483	2026-05-18 15:39:07.367682	2026-04-18 15:55:48.651338	2026-04-23 12:31:54.462736	246	e5a39333-f108-45d7-964b-4e173d0b809c
246	1	TdigbVeiHA1EJjtr/iL2xFkNDDInfjbJDRq3Cl5fu+8=	07471592-b369-475b-b455-62b0f6c5c43b	2026-04-18 15:55:48.652414	2026-05-18 15:55:48.652552	2026-04-18 16:48:19.060888	2026-04-23 12:31:54.462736	249	e5a39333-f108-45d7-964b-4e173d0b809c
356	1	CyZD8LeABT3NWQzIuKzM5BOQZ3XoUvtfYDFszUvW1Xk=	c3a987bc-3709-4f94-b268-324e815b5333	2026-04-23 15:53:11.86545	2026-05-23 15:53:11.865554	\N	2026-04-23 17:53:12.148056	\N	cd103ea6-ece3-4299-a741-99c5f08a4bef
368	7	ckHOS5hjOe80ipwooL8K7sve9LBbol0t9kclrHkec3w=	5fbbc058-fd7a-4d33-b67b-ac7ede0503e5	2026-04-24 10:32:02.91621	2026-05-24 10:32:02.916286	\N	2026-04-24 14:42:37.414458	\N	6b863808-48e9-4f57-a4c6-d35bffe12be4
394	7	LlvT43dFczlRZ+Qd33COJo0BePNkJwSGMfaHrU+cv2g=	cabd6a54-e01e-4368-8560-60aa1fbda03a	2026-04-25 15:12:21.515194	2026-05-25 15:12:21.515244	\N	2026-04-25 16:23:34.680333	\N	7caf6781-3311-4588-ba24-0e41947a2584
386	7	r7nb0Vuc8mVvcSMS9wK+HAW/t9mfVgtxjVg35jBimnE=	b377f035-aadd-4645-8171-e15f11849895	2026-04-25 07:50:02.187214	2026-05-25 07:50:02.18737	2026-04-25 08:20:03.452687	\N	387	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
377	7	z3survCdhKwqfmLeDU8EkCkOirCPCbv8Sha3IloqEBA=	63b4c827-da8d-45b4-b5d9-da7ed5f16828	2026-04-24 14:42:37.885811	2026-05-24 14:42:37.885938	\N	2026-04-25 10:48:04.501053	\N	cd10117b-872a-4b45-943b-5169b28fa884
401	1	EssMXA8wLII7WI9hL70zRSaqvIk7iYWEfgW3OS7UC9g=	f1271f65-833e-4af0-8e79-dc7e929d7507	2026-04-26 10:40:17.583283	2026-05-26 10:40:17.583372	\N	2026-04-26 18:19:48.779828	\N	9f5e66c3-1be2-45a2-a501-edd0437c09f5
412	1	guoUO4zFiHLscK1F5pPtdkSiGp27nY5TeQZeadvxJso=	13ab3ff7-1e88-4aed-87de-4b73f45da722	2026-04-26 17:45:56.866802	2026-05-26 17:45:56.866881	\N	2026-04-27 03:38:52.855305	\N	e68e77f1-6036-4e18-be5c-0a2ff2685b20
409	1	9MfrvrcSopwFOBKREx3/2Vso6Gh1cYoCMN7vDvYFySU=	f2e75167-7659-4b9e-b974-d738a92ed842	2026-04-26 16:08:20.554025	2026-05-26 16:08:20.554109	2026-04-26 16:40:50.715818	2026-04-27 03:08:18.904544	410	515d4f3d-f44c-4dc5-9570-1891b51a79c4
414	1	YIiNuvFUgFCclvI9OxenBmyWBGWSDUfgzGqhesglHWY=	6670c8e3-08c6-49a9-8597-3ae204ee787d	2026-04-26 18:19:49.049349	2026-05-26 18:19:49.049439	\N	2026-04-28 08:58:40.082306	\N	023396f2-a323-4a79-8a44-5e8b113945da
417	1	dTMHnTYILtcJyyB84Pe6MFA21FQIqd9kQ+7vuWvckG8=	07739580-4a8a-4a64-b230-7068a8314498	2026-04-27 03:38:53.090151	2026-05-27 03:38:53.090243	\N	2026-04-28 08:58:40.082306	\N	82e4ad81-5494-4f39-b9f4-50d8630915a6
251	1	3ic9yXDFkpvy5XLbaBfsEKGh3l8/DineUiteoQCYjS4=	3ce0dc02-118e-4d4e-a03e-1f899372a841	2026-04-18 17:02:57.321386	2026-05-18 17:02:57.321513	2026-04-18 17:20:18.821303	2026-04-23 12:31:54.462736	252	e5a39333-f108-45d7-964b-4e173d0b809c
252	1	xu8UMoqDQ4Oannor6X3YnaZOfvooRmy5zU30aP6wdgk=	a7e02121-6c54-4c8d-8d59-ca1cde134f20	2026-04-18 17:20:18.821843	2026-05-18 17:20:18.822039	2026-04-18 17:42:03.999558	2026-04-23 12:31:54.462736	253	e5a39333-f108-45d7-964b-4e173d0b809c
253	1	RWaYrtJuj3vhoZV4mD37b77Gz/Vyoryuk0+odqYRHeM=	8fa1a048-6887-4336-b3b1-0aa43557b33b	2026-04-18 17:42:04.000391	2026-05-18 17:42:04.000567	2026-04-18 17:57:37.248122	2026-04-23 12:31:54.462736	254	e5a39333-f108-45d7-964b-4e173d0b809c
254	1	uScY/cCgO+Q6EgjXbp/xXEqR0onZsfUfcVPdAAEEEno=	f6ca4fde-d9ad-4a6c-bf96-c9b27f310f73	2026-04-18 17:57:37.249493	2026-05-18 17:57:37.249716	2026-04-19 07:01:07.833491	2026-04-23 12:31:54.462736	257	e5a39333-f108-45d7-964b-4e173d0b809c
262	1	zM/OZxdFlRlj5aDwyD7n9+7xZnpPhQ4SE4+wxbEHuU8=	505772d4-7a79-410a-af8c-e191cc0a6409	2026-04-19 07:53:34.970907	2026-05-19 07:53:34.970961	2026-04-19 08:11:08.725733	2026-04-23 12:31:54.462736	265	e5a39333-f108-45d7-964b-4e173d0b809c
268	1	OhQmZpAp4eB+ocwZxnhI4X6Dg0jm5zqmGdG85aoRCvQ=	94c5c25a-fa8c-40d5-9b77-192829730588	2026-04-19 09:02:37.34041	2026-05-19 09:02:37.340696	2026-04-19 09:20:51.745771	2026-04-23 12:31:54.462736	271	e5a39333-f108-45d7-964b-4e173d0b809c
257	1	JT+B7/ysJ2RhnUaLjO1158Wq+bJvTaFq/n4bRM8kNSs=	0d55cf8e-2c96-4a18-94c2-bcee6de6c8b4	2026-04-19 07:01:07.834445	2026-05-19 07:01:07.834594	2026-04-19 07:19:48.586684	2026-04-23 12:31:54.462736	259	e5a39333-f108-45d7-964b-4e173d0b809c
259	1	g1JNS9rR98ay5Q2fwK0qah4DoEuDDetmfTL+stdyy/A=	2e6c5689-e593-4e5d-8377-31bb44b2def2	2026-04-19 07:19:48.587584	2026-05-19 07:19:48.587673	2026-04-19 07:37:41.289021	2026-04-23 12:31:54.462736	260	e5a39333-f108-45d7-964b-4e173d0b809c
265	1	fs3/cohlEhaDkk9AraNSuKk7bUxG+89+TbDH+4hHFoI=	abb08d89-0ba1-4c85-8d2f-afc0e0832e1a	2026-04-19 08:11:08.726838	2026-05-19 08:11:08.72696	2026-04-19 08:47:38.985727	2026-04-23 12:31:54.462736	266	e5a39333-f108-45d7-964b-4e173d0b809c
260	1	kS9D2LWYfcsBj5prvvJ7dows9Z/Nq7AdodP7jqwKj6I=	f833e110-8d74-4a6a-bb92-f6f90b82fe9a	2026-04-19 07:37:41.290051	2026-05-19 07:37:41.290182	2026-04-19 07:53:34.970334	2026-04-23 12:31:54.462736	262	e5a39333-f108-45d7-964b-4e173d0b809c
266	1	qUog7idN1882oer1X9nCw+oAhZe0uY6CeKl0YYSB/XU=	db971ce2-1645-47f4-8eac-e4cbee0b0119	2026-04-19 08:47:38.988792	2026-05-19 08:47:38.988951	2026-04-19 09:02:37.338908	2026-04-23 12:31:54.462736	268	e5a39333-f108-45d7-964b-4e173d0b809c
275	1	8bLWnKnVCzLN2DaUocYvK7Pf6vjqrzWivdENyjsohMY=	b7cbdff5-7f57-4666-9e0a-51896ce2f813	2026-04-19 11:34:43.444242	2026-05-19 11:34:43.444334	2026-04-19 11:51:34.891281	2026-04-23 12:31:54.462736	276	e5a39333-f108-45d7-964b-4e173d0b809c
271	1	PXRy/2Jd1Py4EsXyGTmea5osq9LfUkJqz9yxSJQfzSM=	e240d479-405b-4ae2-b865-d8b81f8352c4	2026-04-19 09:20:51.746537	2026-05-19 09:20:51.746662	2026-04-19 11:01:34.340857	2026-04-23 12:31:54.462736	273	e5a39333-f108-45d7-964b-4e173d0b809c
273	1	3crgUoVTwmuIEWlZt7DP+a5um+NCJLBaWT1Mw3pb+Xw=	9630c13b-5616-4caa-a62c-4f22547ccbb3	2026-04-19 11:01:34.341719	2026-05-19 11:01:34.341854	2026-04-19 11:17:34.790004	2026-04-23 12:31:54.462736	274	e5a39333-f108-45d7-964b-4e173d0b809c
274	1	VebcMsYDDJc4Ew+PkoaCggWeEH+E06oVjnLOnNrsyeE=	87a522fd-8fac-4841-b0a8-2df40a314277	2026-04-19 11:17:34.791139	2026-05-19 11:17:34.791283	2026-04-19 11:34:43.443516	2026-04-23 12:31:54.462736	275	e5a39333-f108-45d7-964b-4e173d0b809c
276	1	BQfmnqvIwJFEsj67xT7rcaXOOe37R87juYkGyXRMIqY=	1e4af6ab-0a1c-48dc-b701-ee0d8dd815fd	2026-04-19 11:51:34.892004	2026-05-19 11:51:34.892118	2026-04-19 12:53:12.888965	2026-04-23 12:31:54.462736	278	e5a39333-f108-45d7-964b-4e173d0b809c
278	1	AZ3bFGExWit7oef5TPRC4NU/5wqL1TCyd/+gC2zWoqk=	e10ef138-457e-4751-be75-61d7c36a1800	2026-04-19 12:53:12.889691	2026-05-19 12:53:12.889795	2026-04-19 13:56:09.830105	2026-04-23 12:31:54.462736	280	e5a39333-f108-45d7-964b-4e173d0b809c
280	1	W7OiB75FLk0ggv837BzbadN8WI5+1NhukbRWTBnlymg=	6db31dee-f5be-4638-8afb-ad48ae9d01de	2026-04-19 13:56:09.830973	2026-05-19 13:56:09.831073	2026-04-19 14:13:19.689212	2026-04-23 12:31:54.462736	282	e5a39333-f108-45d7-964b-4e173d0b809c
282	1	oAxQ0/TF82gUnK8xUNc3nsH1MHnWFarcvakOLak+CrM=	9086254f-d5bc-4d9d-9da9-ee2482d5f6f9	2026-04-19 14:13:19.689617	2026-05-19 14:13:19.689684	2026-04-19 15:46:40.048415	2026-04-23 12:31:54.462736	284	e5a39333-f108-45d7-964b-4e173d0b809c
284	1	VcTyQYhuwxfCyk9p0vwS2m4hN10QdVHeg+aUk7rg6v8=	cf85cbb9-dc73-4de7-a68e-1211685c45b8	2026-04-19 15:46:40.048923	2026-05-19 15:46:40.048996	2026-04-19 16:13:52.858462	2026-04-23 12:31:54.462736	286	e5a39333-f108-45d7-964b-4e173d0b809c
286	1	dnh23V+A2fdnvWnFbT/Ym/1+Wn59E6GXU0vHgPSUto8=	bb6eda2a-26c9-488c-846c-7fcc1a597e9c	2026-04-19 16:13:52.859369	2026-05-19 16:13:52.859521	2026-04-19 16:30:36.971273	2026-04-23 12:31:54.462736	288	e5a39333-f108-45d7-964b-4e173d0b809c
288	1	efIkEm11Bt2AsY0K9EuI8z2qehV/Hs1PH+XgpN539tA=	4f41053c-7320-4f93-be90-7e5086c4a119	2026-04-19 16:30:36.971867	2026-05-19 16:30:36.971963	2026-04-19 16:45:36.111905	2026-04-23 12:31:54.462736	290	e5a39333-f108-45d7-964b-4e173d0b809c
300	1	7FcrOFullW7DWkGUU41udWRdTTI+d6CW52mtJyMSPr0=	f4e48cd9-b290-45a3-898e-e9b1f70daf1a	2026-04-20 09:57:05.738551	2026-05-20 09:57:05.73872	2026-04-20 10:14:48.38115	2026-04-23 12:31:54.462736	301	e5a39333-f108-45d7-964b-4e173d0b809c
290	1	vrJr4ILmLZ/fjSxEMnAYHFDAuavIqGzQyRtUL04B/e8=	a6bdb8ff-8ea7-4abc-a043-2e21dfb51207	2026-04-19 16:45:36.112945	2026-05-19 16:45:36.113187	2026-04-19 17:02:39.798413	2026-04-23 12:31:54.462736	291	e5a39333-f108-45d7-964b-4e173d0b809c
312	1	PXrFPwOAv7w1kshuUqA+ryg9pDQBZAvI4T8lS+VwRjw=	928c9ada-822d-43fb-8ca1-8b3682849885	2026-04-21 16:10:59.040631	2026-05-21 16:10:59.040724	2026-04-21 17:25:07.440534	2026-04-23 12:31:54.462736	313	e5a39333-f108-45d7-964b-4e173d0b809c
291	1	YZJm+6wIMMm13LbHDEVnNYsqc1QlDc66AOdz2z2/TY4=	47e0ee3a-2a95-4b82-8cad-34302e59bf6b	2026-04-19 17:02:39.799148	2026-05-19 17:02:39.799304	2026-04-19 18:16:23.8189	2026-04-23 12:31:54.462736	293	e5a39333-f108-45d7-964b-4e173d0b809c
301	1	g1uGeN0YhRgYb5fzAe5NIbHCAqxG0dvw+YSbO4sFe/w=	f82273d4-e1d8-438b-b261-648d26fef63a	2026-04-20 10:14:48.381978	2026-05-20 10:14:48.38218	2026-04-20 10:43:39.796389	2026-04-23 12:31:54.462736	302	e5a39333-f108-45d7-964b-4e173d0b809c
293	1	bLJADfQ3x+SziPU98hqx34pKhQkeLNb/xBj7tEQXB7Y=	e48db599-13d7-44b8-b07d-c71eef6a194d	2026-04-19 18:16:23.819556	2026-05-19 18:16:23.819679	2026-04-19 18:31:48.049027	2026-04-23 12:31:54.462736	294	e5a39333-f108-45d7-964b-4e173d0b809c
294	1	cmBVTEY7soVrt3mWCT8WoCfigS8q+19zRol1pZuDvK8=	78c1007f-21aa-4583-8701-b60cc0ac79d7	2026-04-19 18:31:48.050497	2026-05-19 18:31:48.050796	2026-04-19 18:49:56.943669	2026-04-23 12:31:54.462736	295	e5a39333-f108-45d7-964b-4e173d0b809c
302	1	1ysROTL5Rl1mS1XY0d7VSn0PrhFhKkraNdqv7KgF8Cw=	23f1a10a-a336-460e-b97b-87c29c892e84	2026-04-20 10:43:39.797393	2026-05-20 10:43:39.79779	2026-04-20 10:58:45.626291	2026-04-23 12:31:54.462736	303	e5a39333-f108-45d7-964b-4e173d0b809c
295	1	qhWxfCOhIeR+dvuugSTbEIfbK7BxkIgEAo4AMsWXzGc=	4d75e0c3-412f-410e-92dd-f2de80f0d539	2026-04-19 18:49:56.944433	2026-05-19 18:49:56.944538	2026-04-19 22:22:27.259339	2026-04-23 12:31:54.462736	297	e5a39333-f108-45d7-964b-4e173d0b809c
308	1	yMMqlnmA1vcbUzEXG+S3oi13B1nW8I9m+rk0cD1XYIQ=	689cacfc-ff6b-4504-959a-fe45e8002893	2026-04-21 09:24:30.833893	2026-05-21 09:24:30.834037	2026-04-21 09:55:06.975191	2026-04-23 12:31:54.462736	309	e5a39333-f108-45d7-964b-4e173d0b809c
297	1	NeWF++wa0odZcRISwkPR53KZkfgaUz4XZS3kLFee9RM=	02683690-98de-485b-b8c1-36f9c814559a	2026-04-19 22:22:27.260348	2026-05-19 22:22:27.26046	2026-04-19 22:40:04.610101	2026-04-23 12:31:54.462736	298	e5a39333-f108-45d7-964b-4e173d0b809c
298	1	75nriqUhitVG4xRKmvQhWCuUsW116cSk6QpkNCQTVhA=	8052f9ee-8b49-486f-88b5-004374975b5a	2026-04-19 22:40:04.610963	2026-05-19 22:40:04.61108	2026-04-20 08:51:06.065275	2026-04-23 12:31:54.462736	299	e5a39333-f108-45d7-964b-4e173d0b809c
303	1	8c9uqFbDowH8giyiXvCYJDw9lThJeRKpB4vo/lc9mlc=	1576b9fa-3ded-402e-b1dc-4906fd3d3fd1	2026-04-20 10:58:45.627308	2026-05-20 10:58:45.627417	2026-04-20 11:49:10.675842	2026-04-23 12:31:54.462736	304	e5a39333-f108-45d7-964b-4e173d0b809c
299	1	E6qaytbPuuSi90EEQid9o0cm/snN0VbaaQQXAfQsw3g=	4e54798a-2cf9-459d-a37c-35c213677fb7	2026-04-20 08:51:06.065929	2026-05-20 08:51:06.066028	2026-04-20 09:57:05.73736	2026-04-23 12:31:54.462736	300	e5a39333-f108-45d7-964b-4e173d0b809c
319	1	Pl1RXf/8ZN2mpmhMtmcoa3deQxTxY/d0GlEyCFnBsZY=	ffbe63fe-feec-47c5-9eaf-11c7071a83ad	2026-04-21 22:19:38.188118	2026-05-21 22:19:38.188211	2026-04-21 23:12:22.132783	2026-04-23 12:31:54.462736	320	e5a39333-f108-45d7-964b-4e173d0b809c
304	1	vYv3JPsYbr5eNjwGVlPmz2URi3GWdnjEpRiyN3jOMgU=	991ef0d5-a144-4d91-ab98-a60ca839cd26	2026-04-20 11:49:10.676616	2026-05-20 11:49:10.676872	2026-04-20 12:12:16.564158	2026-04-23 12:31:54.462736	305	e5a39333-f108-45d7-964b-4e173d0b809c
305	1	iBRQp9G2oTbsp9rbvq6pNBOvH0PSyiy6P5+Mh6ZIEsM=	b343d209-5a47-43ad-9c7b-60581854b130	2026-04-20 12:12:16.565576	2026-05-20 12:12:16.565793	2026-04-21 08:12:18.843161	2026-04-23 12:31:54.462736	306	e5a39333-f108-45d7-964b-4e173d0b809c
309	1	lSBqRMc2bGp7C9QgpEi8DUp6YKiH0fMhHJJnjKZHmU8=	815a3461-a03b-43ed-af5f-74c8f854ad82	2026-04-21 09:55:06.976903	2026-05-21 09:55:06.977036	2026-04-21 15:36:29.00197	2026-04-23 12:31:54.462736	310	e5a39333-f108-45d7-964b-4e173d0b809c
306	1	XTE3xuy9GmWsJ+d1NGlNRMDun1x34CPK8/nGgJbiCM0=	cd44a68c-8053-4949-aebe-723a3fa975d8	2026-04-21 08:12:18.844152	2026-05-21 08:12:18.844431	2026-04-21 09:06:18.170983	2026-04-23 12:31:54.462736	307	e5a39333-f108-45d7-964b-4e173d0b809c
313	1	Zfd9OiJMmOKaB3KyMZ/OIVM0WPx/ggQKhZdfPq59Bpc=	fcc80b6e-f51e-459c-b819-ba5709951aa9	2026-04-21 17:25:07.441303	2026-05-21 17:25:07.441403	2026-04-21 18:04:56.92087	2026-04-23 12:31:54.462736	314	e5a39333-f108-45d7-964b-4e173d0b809c
310	1	jg0f13HmtIUHi8JXvg16cItVSf1WvzMmaUxHISuiTRU=	ab501ede-eae2-4e3f-aa18-cf035b238f15	2026-04-21 15:36:29.002749	2026-05-21 15:36:29.002848	2026-04-21 15:53:08.675566	2026-04-23 12:31:54.462736	311	e5a39333-f108-45d7-964b-4e173d0b809c
311	1	6jDweKH0+lJ90iwCB4iHC0jBtFie059CuJEOEIGP9BA=	22422ed5-fa3d-4eab-94f1-13a0f4b67f55	2026-04-21 15:53:08.676159	2026-05-21 15:53:08.67626	2026-04-21 16:10:59.0397	2026-04-23 12:31:54.462736	312	e5a39333-f108-45d7-964b-4e173d0b809c
316	1	vROYLnsrkVpyM8caI7tOaUwrHA8UhE9bsLhQN12cKRE=	4a08893e-7b8a-4ba9-b649-dfb882e98ec8	2026-04-21 21:11:00.625843	2026-05-21 21:11:00.626059	2026-04-21 21:26:02.286891	2026-04-23 12:31:54.462736	317	e5a39333-f108-45d7-964b-4e173d0b809c
314	1	l1E73fZzviokCq3rJ+UkHlOUlFjG1XGn733mDeHic1g=	6b8dd18e-1391-4bcd-ac2e-4f85dc0634fb	2026-04-21 18:04:56.921485	2026-05-21 18:04:56.921578	2026-04-21 18:52:23.729546	2026-04-23 12:31:54.462736	315	e5a39333-f108-45d7-964b-4e173d0b809c
315	1	oAXz5qEGtgZwrc7OW439Pcr1lGQr/Nc6U3MskEDd9sk=	0cd6a229-7a9a-4d51-b8a4-7f5e76f5caf2	2026-04-21 18:52:23.730378	2026-05-21 18:52:23.730492	2026-04-21 21:11:00.624975	2026-04-23 12:31:54.462736	316	e5a39333-f108-45d7-964b-4e173d0b809c
317	1	s6lfoss4b1D2m+kZ2ZGzgGrbVzhPCgVVrGSxF3DPQmE=	5893176c-fcfc-4479-bf2f-0c44d1353b8f	2026-04-21 21:26:02.287591	2026-05-21 21:26:02.287673	2026-04-21 22:02:23.372179	2026-04-23 12:31:54.462736	318	e5a39333-f108-45d7-964b-4e173d0b809c
318	1	y80vsIfD7+d3mMlKzf/FJa2tCT2DrdXnTGMD/aWtph0=	76500551-e0f7-475d-81cc-732e310606af	2026-04-21 22:02:23.372818	2026-05-21 22:02:23.372922	2026-04-21 22:19:38.187488	2026-04-23 12:31:54.462736	319	e5a39333-f108-45d7-964b-4e173d0b809c
320	1	2cXwVa/EgdX9o5GJA1SqF5YwtgFyBR9BoQwjDaNe1lA=	f429ef35-add0-4352-a416-e37267cac259	2026-04-21 23:12:22.133551	2026-05-21 23:12:22.133634	2026-04-22 07:36:54.045023	2026-04-23 12:31:54.462736	321	e5a39333-f108-45d7-964b-4e173d0b809c
321	1	QIupogWobGmqIafM8v3oHFrekdqi1nwUb7iCFutWIzo=	76204ea5-6016-4e4a-a651-085e3650246a	2026-04-22 07:36:54.045042	2026-05-22 07:36:54.045043	2026-04-22 12:03:47.94666	2026-04-23 12:31:54.462736	322	e5a39333-f108-45d7-964b-4e173d0b809c
322	1	h10W8ywHH+mqAezwOKLc6OlZ2OinwAxY4oxeF7jFswY=	bcbf77a6-28fc-490d-bdf0-85997d189b0c	2026-04-22 12:03:47.947439	2026-05-22 12:03:47.947549	2026-04-22 13:05:49.044915	2026-04-23 12:31:54.462736	323	e5a39333-f108-45d7-964b-4e173d0b809c
323	1	0nWFOnaqheh9uoJ13oNOWvTNBrOxWYiGkKL7VxK+ASo=	c2eb6103-d1ea-420c-8b0e-2c66ca118e8e	2026-04-22 13:05:49.045798	2026-05-22 13:05:49.045889	2026-04-22 13:32:11.020787	2026-04-23 12:31:54.462736	325	e5a39333-f108-45d7-964b-4e173d0b809c
347	1	KzIJ9NBPaYZJiDE1V0ybtvr54vtZJrDfHnuK35eIhGQ=	d2117f3c-1001-4821-bc77-210a99cdb88c	2026-04-23 12:31:54.871107	2026-05-23 12:31:54.871225	\N	2026-04-23 13:44:37.906473	\N	69be617f-274b-43e5-b343-28e0e33dbea9
421	1	4V+QVuFe19ATHOuZ2bnTIuTl4h2KIkzgJCC76K57PNs=	852c787f-0a96-4b25-9798-583d966e06e2	2026-04-28 19:06:37.865872	2026-05-28 19:06:37.865956	2026-04-28 19:24:49.062126	2026-04-29 08:20:56.418468	422	fd0bc5a9-8018-4b1d-8ea3-caebd1b07018
357	1	on2a2ak0EeHWgrAuJGbyDuQJk7mOXTeSx2DvRHYW9G0=	5811024c-ed78-4de1-89f4-0770f2e8fd12	2026-04-23 16:28:17.457381	2026-05-23 16:28:17.457511	2026-04-23 16:44:49.987023	\N	358	17bf74eb-3003-4b64-a855-f5c18fdb6ed6
420	1	sKWizH5dQWETAHR/70hJHUDVtwhVX3dQld7sx3bFUnQ=	a09d16a8-636a-4183-995d-d0e3ac8bd26d	2026-04-28 14:22:04.129044	2026-05-28 14:22:04.129154	2026-04-28 19:06:37.865456	2026-04-29 08:20:56.418468	421	fd0bc5a9-8018-4b1d-8ea3-caebd1b07018
369	7	vpB/BvNRlvfWm+R2msE+V1w0Q7BredYqpNE1suwfhq4=	70905ac0-e0b7-40df-b2d2-dffa4378ed2b	2026-04-24 10:51:00.02686	2026-05-24 10:51:00.026962	2026-04-24 11:05:33.87586	2026-04-24 16:22:18.803954	370	31dc56e8-6552-46fc-ab24-291cc5ffa2b7
424	1	7KzcJ7qFZc9KMRG+cIR6DtydO8Jev55jCRgVHdzKG3c=	8a43dc12-d5d5-4415-b11a-2ecc582ff626	2026-04-29 12:06:52.047338	2026-05-29 12:06:52.047486	2026-04-29 12:50:30.279061	\N	425	1ec4f6f6-b212-471a-9704-f8689e11e9b7
387	7	Mdvgk8/v95SAhB+9UD6V5lY3vod1yvdB5I5duHvHzxU=	570d5291-87bf-49f1-8a6f-ab61cfaa6e2f	2026-04-25 08:20:03.453102	2026-05-25 08:20:03.453168	2026-04-25 08:45:40.701403	\N	388	323e4a37-3ee1-4537-9dff-a8e4e6504f9e
378	7	YE0UEqg7sEgbYEpwmdBlt+fdhch8w5fgmHuBeNhrfIM=	eebe7cf6-4bc2-4ed2-9e81-ae929d24e66c	2026-04-24 16:22:19.149373	2026-05-24 16:22:19.149494	\N	2026-04-25 10:48:04.501053	\N	39926620-2b2b-4710-a9a3-ee7b924497ea
395	7	UnMXywSdwXz+aTCj7+/Ww5qALbFUEfhPVyInmFw4Mj0=	0e022e2f-b328-4484-816e-812141cb575a	2026-04-25 15:27:28.102308	2026-05-25 15:27:28.102405	\N	2026-04-25 16:23:34.680333	\N	4bd96ea2-7ca9-47ff-9330-5c071c08be08
425	1	7XUn2YWbTU2tKKeq2IM79P7DfAW60NZYoIVPaWPbyvY=	fb61d00e-e6fe-46d5-9f5e-7dc16d9ac687	2026-04-29 12:50:30.279471	2026-05-29 12:50:30.279526	2026-04-29 13:45:21.895049	\N	426	1ec4f6f6-b212-471a-9704-f8689e11e9b7
402	1	6tzEfithM/BB1IUhCzn+0c2tq8EPY0bQBuORYPl9iTA=	74bea2e2-7ea2-4ded-ad51-e55d2cdeab05	2026-04-26 11:02:24.231159	2026-05-26 11:02:24.231252	2026-04-26 11:17:33.319528	2026-04-27 03:08:18.904544	403	515d4f3d-f44c-4dc5-9570-1891b51a79c4
410	1	Z/6LiEULto3kDCfhVTc18Pa/6WU5DHW8q30KZaGEfJM=	15a1ff09-f8b2-4c29-8ae8-d41cee26fe0e	2026-04-26 16:40:50.716316	2026-05-26 16:40:50.716389	\N	2026-04-27 03:08:18.904544	\N	515d4f3d-f44c-4dc5-9570-1891b51a79c4
454	1	O1/TPTsXHB5/ATb7remXgDkT9wDT2nP+dU0NMVruzL0=	0c17815f-f4d8-49da-89fe-2112114c4a8b	2026-04-30 19:24:54.766244	2026-05-30 19:24:54.766309	2026-04-30 19:43:03.078593	\N	455	1ec4f6f6-b212-471a-9704-f8689e11e9b7
432	1	fgxkEeN7Ca+GOnIJQFq/l/hipGwQ25wC3i1/BNc+wsU=	a83dd10d-250d-4085-9739-3bbab87ee5af	2026-04-29 15:57:07.960166	2026-05-29 15:57:07.960219	2026-04-29 16:33:53.090549	\N	433	1ec4f6f6-b212-471a-9704-f8689e11e9b7
439	1	JZpAyne6AwwmqTKLnEk2jEGA5eWUHgW0p6F1QiOvVW8=	72ff4124-a808-4776-9b16-9875d3d46221	2026-04-30 10:44:17.026599	2026-05-30 10:44:17.02666	2026-04-30 11:00:55.008092	\N	440	1ec4f6f6-b212-471a-9704-f8689e11e9b7
444	1	8ZRuyUEmKom3UufYLkZKbNoj0SqNGp7kYcE2PUUXsfk=	75b983b0-2404-471f-969c-ba1b478dfd35	2026-04-30 12:40:27.458935	2026-05-30 12:40:27.459018	2026-04-30 12:55:49.447617	\N	445	1ec4f6f6-b212-471a-9704-f8689e11e9b7
449	1	+Pd6OFo7/O4E4vxNIPo7dmO1tGl7Egj9LazTKTulokA=	7e660205-14e3-44fc-90ae-752af5016388	2026-04-30 14:30:10.582001	2026-05-30 14:30:10.582073	2026-04-30 14:45:21.876735	\N	450	1ec4f6f6-b212-471a-9704-f8689e11e9b7
461	1	gW2CN1pSIhyRAPueqAsvzVa/hu2EYRjZs1KLHoZyNuA=	3971528a-4b18-4d88-a6aa-69b6e9ca57a3	2026-05-01 15:11:04.608427	2026-05-31 15:11:04.608428	2026-05-01 15:26:04.97175	\N	462	04dfdda0-3bd5-48d0-9600-8100cd554987
466	1	1VzgZ1lIZ4vX6sGa5nzudc9BVzwpjlInGgsurKoVB1c=	a3bfdc59-d69d-43d3-a2cf-43c8d1e3ca19	2026-05-01 17:12:05.999776	2026-05-31 17:12:05.999861	2026-05-01 17:28:15.461086	\N	467	04dfdda0-3bd5-48d0-9600-8100cd554987
470	1	6nsI+1gKrc3OEwcYexZd7GPkP7MKnKTff4MoHQiEmpc=	227ab7bb-78bd-4c88-9313-5cd8a57d64d8	2026-05-01 18:35:06.178465	2026-05-31 18:35:06.178506	2026-05-01 19:37:40.199997	\N	471	04dfdda0-3bd5-48d0-9600-8100cd554987
478	1	EsYZQn6g1YkIet1G72KxqNN2Z7oPjV6QuRnMHrribhc=	ea07f13f-d1e4-4cc9-9e14-5f2a438371e6	2026-05-02 10:24:01.895923	2026-06-01 10:24:01.895958	2026-05-02 11:01:49.156326	\N	479	04dfdda0-3bd5-48d0-9600-8100cd554987
474	1	FQwgnfJUkEYiRjHPqBYh3U+CFt6Ozzlw3axTadyS3j4=	e4913302-db8a-42fe-adfe-90b1cb99e8b8	2026-05-02 07:47:28.357494	2026-06-01 07:47:28.357494	2026-05-02 08:03:28.660492	\N	475	04dfdda0-3bd5-48d0-9600-8100cd554987
482	1	jX+PXB8u6Ytm5vMhWrjoPiYz98cApTl1SgvtmMh5fqk=	c4d35660-818f-4c32-b96d-e4cdf7270e56	2026-05-02 12:31:04.529778	2026-06-01 12:31:04.529869	2026-05-02 13:00:46.486774	\N	483	04dfdda0-3bd5-48d0-9600-8100cd554987
486	1	Mk7IwRlzL7sIDz3BuCtsWvcyjM0NBbQnbEMzx4N2878=	d9f58fa5-360f-40c4-8561-dcdf4d06722d	2026-05-02 13:40:47.703151	2026-06-01 13:40:47.703204	2026-05-02 13:57:19.698441	\N	487	c60d868a-baa2-4585-be1a-7166d90e04dd
490	1	VnSjDMX0p335eRhkZeGjjmyOMBtZl3GsBloziEM7ZZg=	dd47ae8f-46b8-4ae9-b4a7-e870cb60030b	2026-05-02 14:52:05.133632	2026-06-01 14:52:05.133668	2026-05-02 15:07:18.083409	\N	491	c60d868a-baa2-4585-be1a-7166d90e04dd
497	1	BK4ZkVl7ckSr5CYDuP8d7kGim6R8kaZX/ge3mNG3ZPQ=	c5343e06-006e-49ad-b7bd-1b1d6bed2175	2026-05-02 19:28:37.020134	2026-06-01 19:28:37.020595	2026-05-02 20:04:10.206414	\N	498	8b40d916-37ae-44ac-b486-39436cf99e55
499	1	BHcSkgwN1Ql39zKcJ13eXw9g56lZqgHMKCVaRBXZyjY=	06d84d98-02ae-45ed-97ca-00eda76cb920	2026-05-03 03:14:59.616425	2026-06-02 03:14:59.616462	2026-05-03 03:30:08.311944	\N	500	8b40d916-37ae-44ac-b486-39436cf99e55
500	1	vIRaG4KJIY1sM7GHdfBDy/ESfT2pMja98tfwpKotHsg=	03a1c800-d6fb-4940-9f41-7e365aaed4c3	2026-05-03 03:30:08.31244	2026-06-02 03:30:08.312477	2026-05-03 03:52:29.808377	\N	501	8b40d916-37ae-44ac-b486-39436cf99e55
501	1	P8QULcuP+icn4iFndgeCadZA0OoB6qDEL+raB0HJB+o=	e820f98e-04a0-453c-a94e-326cf3c13342	2026-05-03 03:52:29.808797	2026-06-02 03:52:29.808842	2026-05-03 04:12:32.822128	\N	502	8b40d916-37ae-44ac-b486-39436cf99e55
502	1	QAtw/NGUFRjoff5g9px5SI7+n7MdFkW8xPTlqwyUKOA=	96a6210e-e8b6-46b7-889c-5042fd9b08a4	2026-05-03 04:12:32.823064	2026-06-02 04:12:32.823181	2026-05-03 04:30:20.873848	\N	503	8b40d916-37ae-44ac-b486-39436cf99e55
503	1	36VVI/XdQmfi/j6Yoj5G3ks3yAe7XrJOjPL8ngaVDbw=	f263b88f-7778-413a-b90d-385b6ab6a967	2026-05-03 04:30:20.874053	2026-06-02 04:30:20.874087	2026-05-03 05:55:07.328488	\N	504	8b40d916-37ae-44ac-b486-39436cf99e55
504	1	rsQjzAk8ZkVfck3XFGes286qa8aCuVkxQ6kIqL/CDuw=	40ab7009-8ba2-40a2-b61c-59485286e525	2026-05-03 05:55:07.328744	2026-06-02 05:55:07.328784	2026-05-03 06:21:05.007364	\N	505	8b40d916-37ae-44ac-b486-39436cf99e55
505	1	rBeAdLRqIE3wMgG9QC2Q3T+fqgPw9dUo66WX33UFoc0=	5a2b9d40-12a3-4a97-b3f9-2710b9e213d9	2026-05-03 06:21:05.007833	2026-06-02 06:21:05.0079	2026-05-03 06:47:41.318792	\N	506	8b40d916-37ae-44ac-b486-39436cf99e55
506	1	nRuBw90FFhJdRM0YIqexksjudbpdOotwyIL5bCIW8w0=	4d63e5c7-5fdf-4631-91d0-549284ec184f	2026-05-03 06:47:41.319247	2026-06-02 06:47:41.319317	2026-05-03 14:58:52.067962	\N	507	8b40d916-37ae-44ac-b486-39436cf99e55
507	1	xHC86LJfJeG8/7JU5o7JGiTymSFuqXqA8SWnlGpa+4c=	4300924c-2a4e-44db-a40e-ab761837317e	2026-05-03 14:58:52.068328	2026-06-02 14:58:52.068358	2026-05-03 15:34:47.876434	\N	508	8b40d916-37ae-44ac-b486-39436cf99e55
508	1	xbFjvAJ1QIxGICuuco+GoRlKt9oIACeyC1fQH5OQAWU=	65c1ed08-75bb-4dae-96c2-0a6bf65d1940	2026-05-03 15:34:47.876451	2026-06-02 15:34:47.876451	2026-05-03 15:58:31.424194	\N	509	8b40d916-37ae-44ac-b486-39436cf99e55
509	1	e6bzMdYwvKtS+f5JH8yoiEIwNFiQBJzdN9kIExUeL0c=	ba3255d4-34cf-4350-819e-681a0658b52d	2026-05-03 15:58:31.424466	2026-06-02 15:58:31.424506	2026-05-04 15:57:07.959227	\N	510	8b40d916-37ae-44ac-b486-39436cf99e55
510	1	+CavMN8U6EWY5aKZCqg0xk7U+y6LlVHtyK1NKJNtVrI=	4b83763a-1c67-4599-a639-51608408112c	2026-05-04 15:57:07.960164	2026-06-03 15:57:07.960244	2026-05-04 16:12:12.624445	\N	511	8b40d916-37ae-44ac-b486-39436cf99e55
511	1	TSYyOi07JuIBnIyrXSZr/mp1CsMIpHgk9YGYz2niVMA=	1357b4c6-1575-4098-a069-fa03c89576df	2026-05-04 16:12:12.62546	2026-06-03 16:12:12.625596	2026-05-04 17:49:33.094396	\N	512	8b40d916-37ae-44ac-b486-39436cf99e55
512	1	Hn/rSTvin1dgeIcWPSrnz4VsjTADsbM3pLQcysZBiB8=	a7f1ecc1-138f-44a0-bfd7-769fc8c8a732	2026-05-04 17:49:33.094776	2026-06-03 17:49:33.094845	2026-05-04 18:15:48.268605	\N	513	8b40d916-37ae-44ac-b486-39436cf99e55
513	1	ftuCl2biFpg31RB4eu1/mx3f+capnwwZ3UR8YydDK8w=	fed08177-4140-4517-9500-c52f15ccc85d	2026-05-04 18:15:48.268929	2026-06-03 18:15:48.268994	2026-05-04 18:33:22.078675	\N	514	8b40d916-37ae-44ac-b486-39436cf99e55
514	1	b1u27mJ4Ooie1jY7bRWyVt7Ewn3Kq5BqTsekhPy68Yc=	331147ba-fd42-4cae-920a-697e56e8e6d4	2026-05-04 18:33:22.079435	2026-06-03 18:33:22.079564	2026-05-04 18:48:32.980659	\N	515	8b40d916-37ae-44ac-b486-39436cf99e55
515	1	MhwpSgbCTZMROX70pze4DbJFVETY8eZvfzUP5tkHLqo=	56485566-da9f-47ea-be89-531e1f3c37b3	2026-05-04 18:48:32.981041	2026-06-03 18:48:32.981222	2026-05-05 17:26:18.964115	\N	516	8b40d916-37ae-44ac-b486-39436cf99e55
516	1	kzw85WMG3xW6xpbnBVl4yDkMNWibTYJc2rvmAG8PAq0=	ec4ecdff-dd1f-4b4b-bc33-6dfe81648095	2026-05-05 17:26:18.96444	2026-06-04 17:26:18.96449	2026-05-05 17:45:23.741107	\N	518	8b40d916-37ae-44ac-b486-39436cf99e55
517	7	C3I0nL4PTUQzkeu10nFCbNayv3Ky+2s2AjAcbNdzqkE=	92c7cf7e-9e6b-4e18-acd4-a185aa7ae022	2026-05-05 17:32:02.253125	2026-06-04 17:32:02.253128	2026-05-05 17:48:10.42317	\N	519	bd542f92-6697-4992-92ad-003c7a385ab9
518	1	j46hFDrOqKskgqC/1dfYaWDL7rjj8ziV/+kXq7F5+kU=	37bbff9c-9b72-4447-8529-74756485bbaa	2026-05-05 17:45:23.741924	2026-06-04 17:45:23.742024	2026-05-05 18:15:59.291049	\N	520	8b40d916-37ae-44ac-b486-39436cf99e55
519	7	xA4kfIqREChPwVbHNcgtPQlxoXDCiTNID56qjgCKyDs=	a2cc750e-5ad6-422b-9bbd-8ec7351b89d0	2026-05-05 17:48:10.423181	2026-06-04 17:48:10.423181	2026-05-05 18:27:00.641386	\N	523	bd542f92-6697-4992-92ad-003c7a385ab9
520	1	MswH6mZVJgSBoBrm64PefDbX+Smzl4Mhwy93T4kMpc0=	77b6b0c7-209a-4984-972c-dfe769c3a3e1	2026-05-05 18:15:59.291449	2026-06-04 18:15:59.291516	2026-05-05 18:33:58.138333	\N	524	8b40d916-37ae-44ac-b486-39436cf99e55
524	1	X3rS9DsfI0HUT4zlVSsCgPfdVEw/11Og+0gcsCWBVNU=	2da152f4-87b2-4aa5-851a-12c1a4c4d1bc	2026-05-05 18:33:58.138752	2026-06-04 18:33:58.138805	2026-05-06 08:46:28.795709	\N	525	8b40d916-37ae-44ac-b486-39436cf99e55
525	1	DJKRwLOficljBn7yR4PAx/mMtDWhQtgqR5nceniltN0=	70274dd0-b808-477e-9379-8023fa8e425d	2026-05-06 08:46:28.796042	2026-06-05 08:46:28.796089	2026-05-06 09:01:30.112844	\N	526	8b40d916-37ae-44ac-b486-39436cf99e55
526	1	moRXtkaAd/VyEFfcTglRpIujvAw/ZnfqyriL5Q64ZI4=	182d80b5-79b2-4e08-8c5f-76ba03a8a7e8	2026-05-06 09:01:30.113361	2026-06-05 09:01:30.113415	2026-05-06 10:56:00.133587	\N	527	8b40d916-37ae-44ac-b486-39436cf99e55
527	1	wGzXkgCp9CZar1vcwo7oBOb1bQHSUQN2eaYT2spfvO0=	9695ed02-a678-4534-a403-13cbc7fd6ff7	2026-05-06 10:56:00.133871	2026-06-05 10:56:00.13392	2026-05-06 11:11:46.020116	\N	528	8b40d916-37ae-44ac-b486-39436cf99e55
528	1	3cyklrUQCCjDu1on8Q0MALzNioEYQ6Xj0Q2mLxBtl08=	886f62ec-5a98-483c-a07a-8757f917f540	2026-05-06 11:11:46.020125	2026-06-05 11:11:46.020125	2026-05-06 14:29:33.565996	\N	529	8b40d916-37ae-44ac-b486-39436cf99e55
529	1	KOQGDypFem4X6fAZE0RTeOzLBZCQlN58TBXkA4ytN4A=	d477e354-31c0-4351-90ad-e079e4d6f968	2026-05-06 14:29:33.566522	2026-06-05 14:29:33.566604	2026-05-06 15:22:50.29227	\N	530	8b40d916-37ae-44ac-b486-39436cf99e55
530	1	DHH7g9wgDSxJdIB+ZS4+mFM3pG+jym/8fDxHIZlPdbI=	3cfc698e-2858-4d07-947d-0a5ab23810ee	2026-05-06 15:22:50.29273	2026-06-05 15:22:50.29281	2026-05-06 16:59:14.621012	\N	531	8b40d916-37ae-44ac-b486-39436cf99e55
532	1	KQg0/+OOsBNNcCkc/Fwpj7oidQuCSITHa372qLkVjvg=	6a4ccb8d-ac91-4bf2-81f1-4168e05dabbd	2026-05-06 17:11:37.101772	2026-06-05 17:11:37.101864	2026-05-06 17:29:05.394518	\N	533	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
533	1	22FilK/3BqOcdj8ADpoZEcwJ7fKvY4Pck6nnG5CNsQU=	aa93bc13-c132-4028-a083-553d7029e006	2026-05-06 17:29:05.395023	2026-06-05 17:29:05.395108	2026-05-06 17:43:55.016321	\N	534	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
534	1	+cIeD9PidsTqkmWZFpb5HtG+vhNJNTmB8pSakj3zzSk=	0de09fb3-ede1-472d-9734-e453fb2a37e1	2026-05-06 17:43:55.016801	2026-06-05 17:43:55.01687	2026-05-06 18:28:26.233682	\N	535	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
536	1	dsN5oJxOOXeN4JH+zQMJU1XCfTReG5RjRfHwPu2gNA0=	a3de2a00-ce83-4ff0-b189-bf1f65787bee	2026-05-06 18:45:58.011897	2026-06-05 18:45:58.011951	2026-05-07 12:46:10.386357	\N	537	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
521	1	ILrp6F/w1+GLxJ5LnHkafJxW387NWihYmXSh2uKzQPc=	31e27ef5-5c04-4f48-af68-16a4dea071b5	2026-05-05 18:23:11.275593	2026-06-04 18:23:11.275705	\N	2026-05-08 15:34:04.164402	\N	bd033d85-d594-433d-872c-c6eb3feb2ade
523	7	et9cBtLna6IaI4pwDUsTvR/cZDJJw0SLnYs2qLa/p6g=	64339de3-ce47-46e6-9fef-6d7dd2602afa	2026-05-05 18:27:00.64185	2026-06-04 18:27:00.641953	2026-05-09 12:53:37.754288	\N	548	bd542f92-6697-4992-92ad-003c7a385ab9
535	1	/rH5hlPEk+PYJuh9QvJFASHcM5pQMhIDMDDV75HO/lQ=	ed3a928f-e50a-47a3-a75e-224266f7292f	2026-05-06 18:28:26.234131	2026-06-05 18:28:26.234202	2026-05-06 18:45:58.011072	\N	536	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
537	1	07ZMwce5xq0XmewqfJAczJoQF05Pb8PvbW1NWoM1El0=	1db1bb28-ab75-4c66-ab0c-1ee657fb9aef	2026-05-07 12:46:10.386764	2026-06-06 12:46:10.386821	2026-05-07 13:15:54.38495	\N	538	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
538	1	4uvDjlOwPHEDalnaG150ZFYltVhspQGM+y4Dje3gcE4=	e86e4315-9bac-4d89-9b08-45ddf6d50251	2026-05-07 13:15:54.385455	2026-06-06 13:15:54.385524	2026-05-07 13:36:49.982128	\N	539	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
539	1	0hmOUfqLTB4omJ5OIs6rucGAhv5iIBe5ybRuKv35CZE=	b2a3fafb-6e5e-4314-b7c4-1daee835d699	2026-05-07 13:36:49.983067	2026-06-06 13:36:49.983161	2026-05-07 14:19:47.347851	\N	540	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
540	1	Zu4h9+77V9nXJitzYW2PwpYF9QGrQiBqSvtgsKeoIsc=	e84bd790-0607-4e09-a1dc-90a83b32e083	2026-05-07 14:19:47.348156	2026-06-06 14:19:47.348208	2026-05-07 14:36:18.92411	\N	541	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
541	1	lpzfFAypq1pbyJMUnIIFFLV4M4Y83r7LnRx+Cq7fVWI=	6e4260fc-3b18-40ea-b113-26b103ac400c	2026-05-07 14:36:18.924415	2026-06-06 14:36:18.924468	2026-05-07 16:38:47.955741	\N	542	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
542	1	w99CY8veeAaSv4F+fF2Ym+21rqD3Q+Wn3Ree2v/lyVU=	633f0492-f44e-4aa5-b4de-0b24b2b0e27c	2026-05-07 16:38:47.956304	2026-06-06 16:38:47.956387	2026-05-07 16:53:52.513995	\N	543	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
543	1	7FyosJRN1MXpzzc6eX7mDcCAu4Dlfn53mLX6RP7dJFI=	cfdd945b-c186-47cc-a426-b5a17dc83343	2026-05-07 16:53:52.514406	2026-06-06 16:53:52.514459	2026-05-08 15:33:59.821093	\N	544	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
522	1	iyhIKheUbjkJQDYPHS08fcjcr/FGrUKZudMHk1uSPA0=	0fcd03c3-fac4-4e5e-9251-13b65ec909eb	2026-05-05 18:23:47.004494	2026-06-04 18:23:47.004496	\N	2026-05-08 15:34:04.164402	\N	2dcc5ea2-0d66-45cb-9aba-6e5c28955ff8
531	1	/uwA33TbXL41o6RZEZBPuTq35ZOfFlp+jWwmUxleU1M=	ba412472-f8db-47d8-8f53-f8c787d03826	2026-05-06 16:59:14.621563	2026-06-05 16:59:14.621646	\N	2026-05-08 15:34:04.164402	\N	8b40d916-37ae-44ac-b486-39436cf99e55
544	1	UJH8oBAlsqN4V9hmfteNBFnVlNg2zU6qKud+lLu+ryU=	dd0ebc52-14fc-4afb-9d9d-ce7c4375f9aa	2026-05-08 15:33:59.821372	2026-06-07 15:33:59.821415	\N	2026-05-08 15:34:04.164402	\N	fb78a2c2-6d0a-42b2-8ec6-e69d9e72ab75
545	1	B3/1vXsknuKeLtx0o1MHeRECY/hlf+BdzwbjR21J+hs=	3ab23d61-8cb1-4b11-93a2-1c80267f9ecf	2026-05-08 15:45:27.908506	2026-06-07 15:45:27.908551	2026-05-09 11:43:31.805398	\N	546	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
546	1	fYV37TGvTR6DuFtETbwGY7zYw7sN06h6Dhy4OJzo0+g=	16b6b0e6-6b21-4406-a08e-0856d221eaff	2026-05-09 11:43:31.805594	2026-06-08 11:43:31.805622	2026-05-09 11:59:18.961045	\N	547	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
547	1	g2kJbTGMZiEMu97L7rC8YFcBBQdfyvK9EZ/ephI6L0M=	82d3a68d-4d42-4811-b2a2-a6fbfe60a247	2026-05-09 11:59:18.961519	2026-06-08 11:59:18.961589	2026-05-09 12:53:51.826486	\N	549	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
549	1	xmgXXrwLI59g2NJs48neplUlK1sYY/SXpx7R2QoOqfc=	0a8a8773-3034-4789-8eaf-2242ddaf2f17	2026-05-09 12:53:51.826496	2026-06-08 12:53:51.826497	2026-05-09 13:41:37.422575	\N	550	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
548	7	9H3gBCxuMXqvrvOat7D1Kez5I0Mjc04SRyTkKVumCio=	792e7b64-4e49-4dc8-acaa-180a766335f2	2026-05-09 12:53:37.754309	2026-06-08 12:53:37.75431	2026-05-09 13:41:56.636523	\N	551	bd542f92-6697-4992-92ad-003c7a385ab9
550	1	xkKB1eQm0QhHW0qz0nKWwlR5WK4Alp0AtxxJMhBdcGY=	76d08c5e-3967-4996-8d6d-5647d1771039	2026-05-09 13:41:37.422788	2026-06-08 13:41:37.422814	2026-05-10 07:33:14.832535	\N	552	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
552	1	nVNclS/eqnhdq96EtwOu9CAymc7aFCKyioOFWZACX1A=	f3516185-6f90-45cb-9533-74ef4d496aad	2026-05-10 07:33:14.832987	2026-06-09 07:33:14.832988	2026-05-10 07:54:28.739987	\N	553	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
553	1	VZ7iGr0XMshzrW4tWEe6BQUZRlvDNv+FMj4RAfKN9ho=	012cf4fc-37f9-43ac-84dc-c3666c7f3e12	2026-05-10 07:54:28.740249	2026-06-09 07:54:28.740294	2026-05-10 16:26:55.121617	\N	554	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
554	1	pNAQaBrzSon3n80E19hFElOyvjzzn7pDzUo9gKs1jvc=	f48830ec-bcb9-4f9c-b5ae-4f36326d9211	2026-05-10 16:26:55.121892	2026-06-09 16:26:55.121977	2026-05-10 16:55:01.481731	\N	555	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
555	1	g5jLFyvpy7x7R5hwmPsb/Y7VWhobk2MCkX23GCQBZgA=	4d86815c-8bb3-4dba-ab62-0268396b778f	2026-05-10 16:55:01.482075	2026-06-09 16:55:01.482134	2026-05-10 17:21:04.581217	\N	556	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
556	1	TVyeVRLZmMsiMzbeq8b20RFWCldeZYtcR2CZzXqukYU=	6fd14f91-3e4f-441c-a544-c15c9ec05fe0	2026-05-10 17:21:04.581665	2026-06-09 17:21:04.581746	2026-05-10 17:40:06.197418	\N	557	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
557	1	N/mKLyRJF+nxpajSBu5HXs1IGq9/ImBQELaA8XIWxX0=	e10d5368-2b0a-4423-b0ef-1640235aeee4	2026-05-10 17:40:06.19781	2026-06-09 17:40:06.197893	2026-05-10 18:08:04.278217	\N	558	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
558	1	oU6HSc3pDXwTm6TijhwOk1ujYq/USHmEoDcbcq4g0sg=	478d420b-99ed-4e37-837d-b95528e7b842	2026-05-10 18:08:04.27888	2026-06-09 18:08:04.27897	2026-05-10 18:24:56.912441	\N	559	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
559	1	LDIpADSURyH3yniMfgASRiDRBZkjM5F+cBSBqdHHhd4=	64405d2c-4935-4240-b4ef-5bfcb8a8c213	2026-05-10 18:24:56.913842	2026-06-09 18:24:56.914206	2026-05-10 18:45:25.204164	\N	560	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
560	1	BHVBq2T03SPr0OnXIZA7EE2sucnrqcrCamkPwC2iQQE=	625a5db1-2f57-4ade-886c-371d7fcd62ff	2026-05-10 18:45:25.204453	2026-06-09 18:45:25.204501	2026-05-10 19:06:41.538995	\N	561	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
561	1	dKjTnF6/eg5OCIIWIe0UEuAQNtpW1AIQADEZOc6RE9o=	ec535f5f-700d-4f1e-9f17-abdd75dc5aea	2026-05-10 19:06:41.539268	2026-06-09 19:06:41.539307	2026-05-10 19:30:48.820283	\N	562	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
562	1	eQH29juLZClAlrxcgBmT1JaEHAv/wRUKagU3oOGgm8E=	3b44f94d-3b46-4b35-8781-47bbfd039ca8	2026-05-10 19:30:48.820572	2026-06-09 19:30:48.820625	2026-05-10 19:50:37.624819	\N	563	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
563	1	X1gdyOmI1SRAXB/7PegUY8P/c7J9CRPZhrCKdJhrJus=	857be368-f8ff-4c8d-9df8-23fbd850acb1	2026-05-10 19:50:37.625034	2026-06-09 19:50:37.625068	2026-05-11 02:49:40.574081	\N	564	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
564	1	1gbT10wF87q4jFv4tbrwuFKhtCeOGTXRxsBE7R0Pp+U=	f1df9271-7336-4810-9b97-e779208398f7	2026-05-11 02:49:40.574336	2026-06-10 02:49:40.574375	2026-05-11 03:11:48.713039	\N	565	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
565	1	Q3MXsJRIhqMIP/9S9viSGJf35Dz1/Qc+4vv8hlCZ2eg=	b6d2c612-ef70-420c-98e9-e64ba18ed94c	2026-05-11 03:11:48.713238	2026-06-10 03:11:48.713266	2026-05-11 03:28:35.744848	\N	566	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
566	1	XW3b6CVluLmwtpOVDIslPcxAxj+eDryC8ND5PlFK+dg=	c962efc6-e65e-4ff9-b84a-4916b842daab	2026-05-11 03:28:35.745314	2026-06-10 03:28:35.745372	2026-05-11 03:44:17.62164	\N	567	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
567	1	j/n0/LdQfX0OQlAIn5GqcaZFVHV/lCn9ZQVEy8KQ6vw=	860584fd-bf01-4ac0-8947-50d26482617d	2026-05-11 03:44:17.622044	2026-06-10 03:44:17.622108	2026-05-11 03:58:50.635203	\N	568	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
568	1	AXWiBh3ztqFjYn/G/eKJ2GwJSUA44S6IF268lpp7F8E=	50a0e4f0-a94a-4a4d-815d-74130eb3410c	2026-05-11 03:58:50.635624	2026-06-10 03:58:50.635694	2026-05-11 05:07:58.689077	\N	569	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
569	1	owjz9Y4sp98UHSi5qkDtwxixxvnyNbBdy6zPa8+0vvw=	6e660d7e-178d-4409-bf63-6c9a1eb80d0d	2026-05-11 05:07:58.689502	2026-06-10 05:07:58.689567	2026-05-11 05:27:38.682293	\N	570	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
570	1	gla2uHB+8Id0pKtrXTbBI2IbeW6emOyP/uMUDpYbhSM=	fd5bd95b-821e-414b-a6fa-bf025c3d8978	2026-05-11 05:27:38.682558	2026-06-10 05:27:38.682601	2026-05-11 06:22:29.55042	\N	571	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
572	1	zQRz4IVig5ghoVa/4FzO7V+kM/tnVhQtAX0PjspPVts=	e6cef954-f6f3-447e-bddd-b7ef0c8d2166	2026-05-11 06:37:34.361573	2026-06-10 06:37:34.361624	2026-05-11 06:55:14.115503	\N	573	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
551	7	L33keEpa0PulBJTaDs5pUvcbW1CPZ3capFClxNZcdG8=	35ac9fc0-d485-4710-9a34-ec6b1fc54d58	2026-05-09 13:41:56.636532	2026-06-08 13:41:56.636532	2026-05-11 19:04:10.84864	\N	580	bd542f92-6697-4992-92ad-003c7a385ab9
571	1	3YdqEg6e7sDmW2UmmyTtls5xR/4suKE3VHDVVNqSfHU=	51b5e793-85f0-45bb-98ef-08d38ead73e0	2026-05-11 06:22:29.55081	2026-06-10 06:22:29.550856	2026-05-11 06:37:34.361194	\N	572	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
573	1	SA2LtH6g+J4cl2E3lr5l1dXjZHGCTUzhxmJtT7DAsj4=	b8601eb2-8fd8-44e7-be55-e1234a584b11	2026-05-11 06:55:14.115941	2026-06-10 06:55:14.115983	2026-05-11 07:10:20.615417	\N	574	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
574	1	coMMYBQR8ar/xieJYrtgiGRxglg9w7d+/nGwUJBs1yY=	5c59fe42-372a-418b-806a-edd2a0d33f0d	2026-05-11 07:10:20.616032	2026-06-10 07:10:20.616074	2026-05-11 12:34:02.173129	\N	575	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
575	1	icJUgoqggE4q+IO2COPLZjhqXYidKQ2xCdVdvM+A6fY=	6f1a5f52-9bdd-4470-ab39-70169d9d539a	2026-05-11 12:34:02.173567	2026-06-10 12:34:02.173611	2026-05-11 13:10:33.148611	\N	576	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
576	1	jo9VO+0tGbnWQRbMqVJBv0cgE/IJfDl5VkRp5Oj3Q4M=	57923488-c4fc-4fd8-bdd2-7236d888b81b	2026-05-11 13:10:33.148909	2026-06-10 13:10:33.148939	2026-05-11 13:30:22.495917	\N	577	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
577	1	o4GM+h+zXZxbAOazylyXLBP8U/PMMMKxfKVhJzrIqfA=	19e75414-6c5e-4543-b1ec-b894b55dfddd	2026-05-11 13:30:22.496333	2026-06-10 13:30:22.496403	2026-05-11 13:47:38.912769	\N	578	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
578	1	y1nnT3aZXT0B1eLck1YCQJJ41ApCg8BmAvMImFf90hs=	c34fbe59-8f29-4893-8745-e35017f9d53d	2026-05-11 13:47:38.913397	2026-06-10 13:47:38.913447	2026-05-11 19:03:42.542322	\N	579	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
579	1	evczasWChVvjrpIUzkJFliWBJj+3jDMuHXGs/qcg6xU=	3c0bd217-14b0-48b1-9093-6ad39bf86e57	2026-05-11 19:03:42.542731	2026-06-10 19:03:42.542781	2026-05-11 19:25:05.142793	\N	581	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
580	7	1rz8tLm/iQO5ctIMcLH5dmyoK4L5PU2FUOpZa/Qrhi8=	95591110-7010-4aa5-bbf9-b5171b692873	2026-05-11 19:04:10.848646	2026-06-10 19:04:10.848646	2026-05-11 19:25:26.258394	\N	582	bd542f92-6697-4992-92ad-003c7a385ab9
581	1	/FvZt/IOekCcyrrEDVaGdkAqglM2OR6Oae/K1lpu1yg=	51a0a346-8c0c-4f38-90cb-bc25cd745e73	2026-05-11 19:25:05.143348	2026-06-10 19:25:05.143459	2026-05-11 19:43:05.661836	\N	583	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
582	7	Gj7hKsgBwvEXn/HHObx+yajmJnByIF9qOi/5Ox8pqVE=	322f6cef-ba08-4907-a9e4-c7b546e0fe29	2026-05-11 19:25:26.258405	2026-06-10 19:25:26.258405	2026-05-11 19:43:27.820131	\N	584	bd542f92-6697-4992-92ad-003c7a385ab9
583	1	6ZAY76WVJmlVmciEzLtlHxyTy9SJ57uhymWeWFcyaVY=	4f9cfcc2-770f-4f73-8236-d7914808ad2a	2026-05-11 19:43:05.662407	2026-06-10 19:43:05.662457	2026-05-11 21:29:30.96299	\N	585	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
585	1	EvgnrLFEnjb19R43VI8hVA+UpZM7EPp9AffiYji50gM=	b8b25964-01fe-40e7-9796-352dc9cdca42	2026-05-11 21:29:30.964022	2026-06-10 21:29:30.96409	2026-05-11 21:44:27.071923	\N	586	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
584	7	+Ph9Crk2ucBi4CrA0hj0u/5Dxd2t4htyPu91dI15ogQ=	40584cc6-793f-4ace-87d2-993395ed0a1b	2026-05-11 19:43:27.820143	2026-06-10 19:43:27.820143	2026-05-11 21:49:21.731501	\N	587	bd542f92-6697-4992-92ad-003c7a385ab9
586	1	F/VD106Aw1BREKAGFWLTOktpgKThrOZOSIq/J4hJHaM=	abdf3a22-6c4c-4e81-93c1-87e59781e834	2026-05-11 21:44:27.072262	2026-06-10 21:44:27.072356	2026-05-11 21:59:33.049109	\N	588	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
588	1	oXlnvsEa4hBanZ20vtAaV7aTx+5FZNGyiGTtC0nhcwQ=	f61ffb08-8e96-4ba2-a304-7b642a89c3ac	2026-05-11 21:59:33.049505	2026-06-10 21:59:33.049557	2026-05-11 22:27:51.57497	\N	589	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
587	7	LBk9R1dF7DvUGh7EDg7s8yNbT8HDsHY+DXHoByBwbr8=	e391e9aa-ebd7-495c-a5a1-4c9ad1f1ba11	2026-05-11 21:49:21.731513	2026-06-10 21:49:21.731513	2026-05-11 22:28:53.350885	\N	590	bd542f92-6697-4992-92ad-003c7a385ab9
589	1	JCupGcRvfLtb9k8UnwhGSXwYtAACkQ5SZNDzXqzGArk=	67d128d8-44ca-4c01-86f6-f3ce0aa23a29	2026-05-11 22:27:51.575854	2026-06-10 22:27:51.575952	2026-05-11 22:44:19.895424	\N	591	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
590	7	paW6WTKIv7LDZaOgfO1Km+vV9ZOPv67sEJ/SpzsmDa0=	9ad824c0-2842-4f0a-a433-1800a3b3ebdc	2026-05-11 22:28:53.350895	2026-06-10 22:28:53.350895	2026-05-11 22:44:31.698498	\N	592	bd542f92-6697-4992-92ad-003c7a385ab9
592	7	AvtORFG6IOIs1yyt2qoV09/rChet16wvunrWBVRlXWQ=	5862ae28-3a6c-4d63-95d7-44d7b8a6ea06	2026-05-11 22:44:31.698508	2026-06-10 22:44:31.698508	2026-05-11 22:58:31.604022	\N	593	bd542f92-6697-4992-92ad-003c7a385ab9
591	1	QF/MASHH/RZm1HCFeKeqPGy20jEYyfPV5b0+NwAuWGc=	025f224d-631a-43cc-9aef-4ab560495447	2026-05-11 22:44:19.895941	2026-06-10 22:44:19.896038	2026-05-11 23:06:06.202202	\N	594	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
593	7	IuQqs7RGrdgsF0ETiz+7/agSEsNPW2J5HSP0sqAIGGc=	e5eef0e9-9ffb-4dee-9496-87db35aaec8a	2026-05-11 22:58:31.60443	2026-06-10 22:58:31.604522	2026-05-11 23:15:59.835511	\N	595	bd542f92-6697-4992-92ad-003c7a385ab9
594	1	m51e2E2nc6CSmVO2qWJNF/rDuJnSBCPIF+nxbyeOFeY=	dbaf9ca3-2c9b-40ac-b23f-9fb1d9c0a899	2026-05-11 23:06:06.202645	2026-06-10 23:06:06.202708	2026-05-11 23:21:02.852616	\N	596	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
596	1	NSjpy7Uxyh2U1pgEKR9RQsym6EfkRyGwkCBTdudjyrk=	5e81e2d0-465e-4819-a61b-5e352ca4f701	2026-05-11 23:21:02.853011	2026-06-10 23:21:02.85307	2026-05-12 00:11:37.803942	\N	597	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
597	1	32wMlhKP0sRzgj7G6yQa0RKJTNlyqXV4yYAFPW8ly40=	7e49dc95-c743-4844-ae0e-8ddaa64eed28	2026-05-12 00:11:37.804222	2026-06-11 00:11:37.804272	2026-05-12 00:35:54.613627	\N	598	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
598	1	Cs7HPLatKsyBBEoBD8n7tjC12j/3F7z68aSdaKHFDY0=	86093dbb-75df-4f9e-be70-6780195c009a	2026-05-12 00:35:54.614148	2026-06-11 00:35:54.614238	2026-05-12 00:54:10.840979	\N	599	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
599	1	RJ5qRO0BD10kuTD8Z3KtInkiNotfKMvqI2DrkR2Bo74=	02f9b1a7-91d6-422f-84e6-7dae2cb335d0	2026-05-12 00:54:10.841409	2026-06-11 00:54:10.841449	2026-05-12 01:12:27.749684	\N	600	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
600	1	BtxPFLbAZ5i3DOYbs/2vZE/9AFcrMH1CRj3QReiI/Qc=	ae006131-a85f-4301-94ff-fff46bdaec0b	2026-05-12 01:12:27.749945	2026-06-11 01:12:27.749987	2026-05-12 01:43:34.323206	\N	601	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
601	1	MtqORINaE+3I5VvSAWwLs+qBpY9Aie5SVDGYX4V6ugY=	d60f85ad-379c-4931-b939-7a2671ea9f55	2026-05-12 01:43:34.323673	2026-06-11 01:43:34.323755	2026-05-12 02:08:54.725609	\N	602	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
595	7	y0yiumN/LtYM+cMEnaXOjh3rIENCJBJRnSpOtWniytc=	a19d3d69-b488-4bc7-9ffa-f9335976bf03	2026-05-11 23:15:59.83584	2026-06-10 23:15:59.835883	2026-05-12 02:13:16.164005	\N	603	bd542f92-6697-4992-92ad-003c7a385ab9
603	7	oZlPfSG3N4Bs1FsKljVQQRpihmrez7o1kdeayrjMw/8=	155a8994-dfca-4a0c-9eb4-42ff8378f3a9	2026-05-12 02:13:16.164812	2026-06-11 02:13:16.164877	\N	2026-05-12 02:15:26.615006	\N	bd542f92-6697-4992-92ad-003c7a385ab9
602	1	nRVaXLNH1Nr7pN8wRWXCnuAKOMHmdaa4MKLPGjTxQQA=	4714b348-edaa-4b2d-8efa-692ffb81ce7c	2026-05-12 02:08:54.726044	2026-06-11 02:08:54.726226	2026-05-12 02:32:25.934874	\N	605	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
605	1	/8rZvv+TIxw3NdA7SrIXbFate9FKJoOTHnnyM8PkGY8=	3cc9dc38-fdad-48d8-a779-d57bc3e9b01b	2026-05-12 02:32:25.935179	2026-06-11 02:32:25.935291	2026-05-12 05:03:46.532522	\N	607	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
607	1	y9B0LT/q1ZpiWLl47TRl2OjiFA1lY+vtJ95VAU7fHXA=	f69bb136-ed5c-45f9-83ac-90b6eef6655d	2026-05-12 05:03:46.532777	2026-06-11 05:03:46.532816	\N	2026-05-12 05:03:56.048538	\N	6f014ef8-9e78-47ee-a245-d3dd7dd7c6df
606	7	TCA4mQjvFpIXjaQLuisxAuWfjXKpxWQ4nh4eK19QdNU=	daef6f05-c04b-4193-a33d-b1d2a8adafa3	2026-05-12 02:32:47.938114	2026-06-11 02:32:47.938114	\N	2026-05-12 05:05:41.725627	\N	1dc073f9-de68-49c6-a4f2-99b1883c8078
608	7	cPKNpCUg3Kw2Zvzw3fzLIhBzotltRCtNQSWpBUHhy6U=	85963b03-4534-465e-99b3-12b5d448da00	2026-05-12 05:04:02.095138	2026-06-11 05:04:02.09514	\N	2026-05-12 05:05:41.725627	\N	108e3c62-df38-4362-844a-0a252901e31c
604	7	lDj7+BlJsm1WdKT3flSuj86sF1b7aDksG8IHq56YfnU=	452834dc-d0b6-4b7b-915c-06cb2b161440	2026-05-12 02:15:29.849967	2026-06-11 02:15:29.849969	2026-05-12 02:32:47.938102	2026-05-12 05:21:15.125294	606	1dc073f9-de68-49c6-a4f2-99b1883c8078
610	1	jOU1bFxN5S2j0Cu6GCR9NhjXlOXrs4tQVExDqIU+geE=	fbfa2d8f-876c-49a3-ac72-ca422a654b2e	2026-05-12 05:20:38.888967	2026-06-11 05:20:38.889013	2026-05-12 05:48:24.742132	\N	612	b591f085-0099-4c32-95e1-a41c95b1e479
609	1	t8mTQb3jz/iYk0aXeT2uSiePksJKpzIppG2mCSSvPCc=	19ce3574-39a0-4ae5-abc3-7a6e38130d28	2026-05-12 05:05:45.882543	2026-06-11 05:05:45.882545	2026-05-12 05:20:38.888704	\N	610	b591f085-0099-4c32-95e1-a41c95b1e479
612	1	IIQ8cKKL5H038jPmgH5t0gUfDGBaHAy58tig+x+OuPg=	955c381d-4bd4-490e-9fd7-6c6507ae11e5	2026-05-12 05:48:24.742394	2026-06-11 05:48:24.742436	2026-05-12 06:04:00.698759	\N	614	b591f085-0099-4c32-95e1-a41c95b1e479
614	1	royK7jvUu00WQWLdiBxU2sOgjU2cV0JlGh2xui4gAzM=	5c741707-0b01-420c-9b65-0d3735af991b	2026-05-12 06:04:00.698769	2026-06-11 06:04:00.698769	2026-05-12 06:19:01.087708	\N	616	b591f085-0099-4c32-95e1-a41c95b1e479
616	1	Ph/U7le88Oh/Oa0aMIm7IWou3tc7XsWSEJRXE33aKy8=	dcc7e5d7-37ab-40dc-a40d-68d4ddaa27f4	2026-05-12 06:19:01.087718	2026-06-11 06:19:01.087718	2026-05-12 06:38:49.845109	\N	617	b591f085-0099-4c32-95e1-a41c95b1e479
617	1	2o2Wemsvf1MtK3L4LGyxjMHvoSLoWSMPJ4svEz43vME=	0f77bf32-3b2b-4590-814c-57870a7e544f	2026-05-12 06:38:49.84611	2026-06-11 06:38:49.84623	2026-05-12 07:23:26.513077	\N	619	b591f085-0099-4c32-95e1-a41c95b1e479
618	7	CLsMhQ3+v7iuZC8SIJEnyUhUxUHm+ieKgsdd0CwqKUo=	13f1927a-3b79-4716-af17-cf6c461f2961	2026-05-12 06:39:10.401392	2026-06-11 06:39:10.401392	\N	2026-05-12 07:24:18.517792	\N	27fb5b3a-225e-4e20-81ce-ac0a8adbac2f
619	1	bECfjJrUaWcwoVJ8jvxDxt6ratpZaMFwfQoi5BbGpMg=	a9a79506-8579-4eae-a515-6725ecc0e733	2026-05-12 07:23:26.513698	2026-06-11 07:23:26.51377	2026-05-12 07:39:06.930297	\N	620	b591f085-0099-4c32-95e1-a41c95b1e479
620	1	WazckFerpHR9CqZ7ZdeyIEoZub7DAUlen1Yn4NIAhlE=	fd5a9515-7a7c-4301-b5d0-77394ce28c20	2026-05-12 07:39:06.931008	2026-06-11 07:39:06.931138	2026-05-12 07:54:20.393438	\N	621	b591f085-0099-4c32-95e1-a41c95b1e479
621	1	J8n9iZDOzDGMlcdRzjU0P/spMiSiaYWuZEFnbqCAa0g=	40bc847c-2c35-4387-bdd9-3b3cb534cd36	2026-05-12 07:54:20.393632	2026-06-11 07:54:20.393728	2026-05-12 08:09:22.660428	\N	622	b591f085-0099-4c32-95e1-a41c95b1e479
611	7	jLmjtML9f+ifeVU3m4E3TE2HkogDFerGbTLVkcqgXGQ=	6f5549c5-ee2f-4a41-88cd-0723cb1d57e8	2026-05-12 05:21:18.996802	2026-06-11 05:21:18.996803	2026-05-12 06:01:54.961736	2026-05-12 08:16:15.10182	613	27fb5b3a-225e-4e20-81ce-ac0a8adbac2f
613	7	lmNyfKL+8HkjGsGO0etBFHkl4rHlgjnNejdMuGkR+z0=	d9760d22-dac0-4998-b973-4cde33eecd34	2026-05-12 06:01:54.962261	2026-06-11 06:01:54.962332	2026-05-12 06:18:01.099788	2026-05-12 08:16:15.10182	615	27fb5b3a-225e-4e20-81ce-ac0a8adbac2f
615	7	c6W0Rc0Tqa7DW5FOViSr/LkG5hOUZj2Th6Jf6wsy3og=	bc7d6692-fb7e-49e8-92d7-370990600499	2026-05-12 06:18:01.100275	2026-06-11 06:18:01.100346	2026-05-12 06:39:10.401383	2026-05-12 08:16:15.10182	618	27fb5b3a-225e-4e20-81ce-ac0a8adbac2f
622	1	q5LnuZMJGSel6z15xaU77I2XIrRGrkHZZZKE8yRbeP4=	f293f5db-81a7-4c08-aa18-7b308a167da1	2026-05-12 08:09:22.660877	2026-06-11 08:09:22.660947	2026-05-12 08:24:22.868542	\N	624	b591f085-0099-4c32-95e1-a41c95b1e479
624	1	RXbGt3I3SJDT/y/dElqE346/IMhmxXEykH+iImHOIeM=	2494eabb-e5b0-49c7-b022-8f0d8255323c	2026-05-12 08:24:22.868752	2026-06-11 08:24:22.868778	2026-05-12 08:45:00.940479	\N	625	b591f085-0099-4c32-95e1-a41c95b1e479
625	1	wKy3t4kOUJ7W6HVdbo6Vg4WJ5TPSPwOmWH5vIAx8wJY=	4f42d2c1-e8c7-4a7e-b019-b821df18660b	2026-05-12 08:45:00.940917	2026-06-11 08:45:00.940995	2026-05-12 09:07:12.990796	\N	626	b591f085-0099-4c32-95e1-a41c95b1e479
626	1	mnCrVMQhxYdmIpxEIXcD570nJ9hlgYoZjRkf4BYdJ7c=	710f7ce9-4f88-451d-99ea-78d3e8997ff6	2026-05-12 09:07:12.991124	2026-06-11 09:07:12.991174	\N	2026-05-12 09:10:29.048992	\N	b591f085-0099-4c32-95e1-a41c95b1e479
623	7	4Wo2T5dO8wqDFvJhXUDFQTForWZvSiu0Lj9rlPVwQIc=	9b346432-9c9b-4923-812c-9b9db3c56d25	2026-05-12 08:16:20.502068	2026-06-11 08:16:20.502186	2026-05-12 09:15:33.468743	\N	628	3f176c24-2d94-4689-8649-47ee11d2db49
628	7	sVunl8MxVsPNQFaL9n9gB5ugIFbp8sSVA/WEEde/lRg=	982a6058-aa5b-4abc-b7ed-23474e21971b	2026-05-12 09:15:33.469291	2026-06-11 09:15:33.469388	2026-05-12 10:04:31.114911	\N	629	3f176c24-2d94-4689-8649-47ee11d2db49
627	1	VIozOQHuuCNVLs80ptNkoEEuPHru6orQmRjdojS7X9U=	c9fa7330-d7e5-4ab7-8d17-511193e2fd5b	2026-05-12 09:10:33.521182	2026-06-11 09:10:33.521183	2026-05-12 10:46:20.641469	\N	630	541e1c39-0984-4194-8795-13fcf6e771e9
630	1	2q/09i3eNd2R8yuzlQC6B6eIcbemBWAKzF9MWBP3fnw=	7344a57b-1bab-4841-9faa-372ebd582cc0	2026-05-12 10:46:20.641834	2026-06-11 10:46:20.641879	\N	2026-05-12 10:47:36.90135	\N	541e1c39-0984-4194-8795-13fcf6e771e9
631	1	zmyOltH7MB1r9X/xGjD2XqZ4xkgLVH2fkYp7AEuSYYM=	2859a80b-69b9-488b-ac05-49ed0fffd437	2026-05-12 10:47:41.412726	2026-06-11 10:47:41.412769	\N	2026-05-12 10:48:44.256805	\N	c8545d34-8df3-4115-8f1c-5569ce9b5de2
632	1	mH7Lyfb+7Xu5KhoGuvN5SLrxpHc9t2HCdHwBBi2iNbY=	5001c76d-8efc-437d-b39d-80725aead3d9	2026-05-12 10:51:49.997983	2026-06-11 10:51:49.997985	\N	2026-05-12 10:53:28.252921	\N	d70f24e3-573c-4eb3-9e38-f4cbdaef0c4e
633	1	vIv1ltj16ifmZ2zrslKOsUyjASmpvstNQGlmvmfvpzI=	42b1b736-cb34-404e-9be5-1739f6e9ca65	2026-05-12 10:53:47.9703	2026-06-11 10:53:47.970301	\N	2026-05-12 11:00:43.253564	\N	0018201e-06c0-4bac-af36-c3117e40e402
629	7	+xcw/CKdwKIpHlYAfJrj+DVxwmYGAGPTNNLpN/HZ/s0=	e2a827fa-d36c-4656-a008-ad12b8666b02	2026-05-12 10:04:31.114931	2026-06-11 10:04:31.114931	2026-05-12 11:05:10.37927	\N	635	3f176c24-2d94-4689-8649-47ee11d2db49
634	1	VVe/K67oCEw9h90BhvAK0gwMAR2kd7vIfToOVfyiN9E=	dc4e68cf-8d44-483e-b785-a5a629cd2360	2026-05-12 11:01:03.811317	2026-06-11 11:01:03.81136	2026-05-12 18:04:09.655942	\N	636	e7781b00-21cb-4a5e-aa26-710e5b73210d
636	1	Ly1BatmIxtJsLssXPSShzS/z7RBKJHa/vvjnQ6ioZrc=	480bb43c-871d-4218-8daf-1074baf4d51f	2026-05-12 18:04:09.656141	2026-06-11 18:04:09.656169	2026-05-12 18:25:56.977805	\N	637	e7781b00-21cb-4a5e-aa26-710e5b73210d
637	1	BjCuEUxeP+akPUX0jibG2nCGhWPrjxZMObPS8ZC2RR0=	87c098b6-37a5-4f9c-ba7b-1c4e028e0303	2026-05-12 18:25:56.978062	2026-06-11 18:25:56.97811	2026-05-12 19:11:52.880492	\N	638	e7781b00-21cb-4a5e-aa26-710e5b73210d
638	1	yd70l6aT7dN9asqT0tW0ZBetyhnI4VnSVNRjUULp8N0=	f3114555-632e-4b6d-a2ae-900f45e66d12	2026-05-12 19:11:52.881112	2026-06-11 19:11:52.881194	2026-05-12 19:26:41.080184	\N	639	e7781b00-21cb-4a5e-aa26-710e5b73210d
639	1	mDSXSQouWna+S+nVPSShkDMKTnjanjg8JTtvrQBO3qA=	a323495a-f7df-4f81-a615-973daee9253d	2026-05-12 19:26:41.080564	2026-06-11 19:26:41.080615	2026-05-13 07:22:13.780418	\N	640	e7781b00-21cb-4a5e-aa26-710e5b73210d
640	1	iMiIfVLEqag90tA3iGBJ/Mb3zJPoZyC/RJquwgRJW+0=	1369c9b3-1b4e-4bd7-add5-4ab1070e6955	2026-05-13 07:22:13.780735	2026-06-12 07:22:13.780785	2026-05-13 09:23:52.371297	\N	641	e7781b00-21cb-4a5e-aa26-710e5b73210d
641	1	gDMZNxxZmjkwLsQbLd6NWUt65HUWIM1rS12gN3745G0=	75633a5b-1bef-4cc1-94eb-0759c5458c79	2026-05-13 09:23:52.371758	2026-06-12 09:23:52.371845	2026-05-13 09:45:58.615177	\N	642	e7781b00-21cb-4a5e-aa26-710e5b73210d
642	1	qsDXqIGU9pRrLJl2TiiGXjrtRpz8Br5SMXHeStXJTBQ=	a1cc5206-1d57-40f0-aa85-be9ab33dfa43	2026-05-13 09:45:58.615357	2026-06-12 09:45:58.615384	2026-05-14 07:26:58.735967	\N	643	e7781b00-21cb-4a5e-aa26-710e5b73210d
643	1	Mb+/ZdM2NAW7TXvi2/xyRbusXEv9AnbuCti2vm45VNw=	f6e9ca93-3a30-4dc1-9a1c-0513042f626c	2026-05-14 07:26:58.736529	2026-06-13 07:26:58.736586	2026-05-14 07:50:47.818781	\N	644	e7781b00-21cb-4a5e-aa26-710e5b73210d
644	1	B/7Llzyn8LtCYw97eNd3i2fhCfrsE/ol8dO8yDCuLgU=	328f3654-0a8b-47f6-b5cb-7875cf68beee	2026-05-14 07:50:47.819102	2026-06-13 07:50:47.81915	2026-05-14 08:13:09.760342	\N	645	e7781b00-21cb-4a5e-aa26-710e5b73210d
645	1	1HcegMQi4G1dFq8fQREv/FCVVY2zt+3xbuiSeimDqKE=	9ead723f-2b0b-41c7-88cf-f4f64f982afb	2026-05-14 08:13:09.760542	2026-06-13 08:13:09.76057	2026-05-14 09:43:12.64587	\N	646	e7781b00-21cb-4a5e-aa26-710e5b73210d
646	1	Jq3ElBK6rvvSatDOB9kF0lKs9qZZbPV0Tq/Pi5f6hxo=	8534ebb3-ad15-4d37-8c1f-2a708a681c2d	2026-05-14 09:43:12.646225	2026-06-13 09:43:12.646279	2026-05-14 09:58:42.303567	\N	647	e7781b00-21cb-4a5e-aa26-710e5b73210d
648	1	O2dRm0K4LRwLtaIt6FWA5c5WKnetNfRUXbOCJLOuC1o=	65c2c653-b3e3-4db6-8f8a-19ab9f8e4244	2026-05-14 10:18:16.58075	2026-06-13 10:18:16.580777	2026-05-14 11:12:11.251634	\N	649	e7781b00-21cb-4a5e-aa26-710e5b73210d
635	7	7Kx3O7BhK+BEcrFC60ilTIwTrkGKgwpe47K9ZAkNU5s=	d362e4a2-9b1c-4200-afde-08dc393a137c	2026-05-12 11:05:10.379465	2026-06-11 11:05:10.379466	\N	2026-05-14 11:20:35.942686	\N	3f176c24-2d94-4689-8649-47ee11d2db49
647	1	nQewn1+iov1gNa8pxRMGS6ZysHBw2FF9F4cxXb549KQ=	cb38291f-3fd7-4af3-92f6-044ad519f0d5	2026-05-14 09:58:42.303962	2026-06-13 09:58:42.304029	2026-05-14 10:18:16.580565	\N	648	e7781b00-21cb-4a5e-aa26-710e5b73210d
649	1	ByE6CEPb/Q8LtFLauJvmHpilPJe7mzUPcmdv9w6lef8=	86a17e5b-52b1-4c6d-a728-9defe74284fc	2026-05-14 11:12:11.251854	2026-06-13 11:12:11.251888	\N	2026-05-14 11:20:20.682451	\N	e7781b00-21cb-4a5e-aa26-710e5b73210d
650	7	uZNp+XVszOSwWMyl0AiR99FhesjkjdafVTbRY1MZH3s=	6668c5e8-1d94-4c1d-af1c-4b5233629313	2026-05-14 11:20:27.616835	2026-06-13 11:20:27.616881	\N	2026-05-14 11:20:35.942686	\N	2d90dd2a-befd-417c-a6b3-ce317881c02b
651	7	DZxuHf74Ps3CdeV8ZPHk8apidrCCnQsKRCo13cUwFR0=	fdf49aed-945a-40ee-92fd-b34eed295173	2026-05-14 11:20:40.049629	2026-06-13 11:20:40.04963	\N	2026-05-14 11:24:58.036739	\N	7303a1b8-6eef-423e-a77a-4c0579809b54
652	7	8junBG2l5G8pSKlXh3U9YPcy3ZXOl1Bp2tIFrdqjwOU=	4c77b208-81ac-446b-a333-1c36beae6f8f	2026-05-14 11:25:01.638468	2026-06-13 11:25:01.638515	2026-05-14 12:04:05.746023	\N	653	aee1b760-5829-4f23-bb3d-d084b4ccb43e
653	7	VYs+ypmVHaGugee7nkJMHfQAJqd2rxPUgxhi9ojEqjM=	00dd5d68-e1b2-46d6-bebb-74dec4161761	2026-05-14 12:04:05.746298	2026-06-13 12:04:05.74634	\N	2026-05-14 12:11:04.846033	\N	aee1b760-5829-4f23-bb3d-d084b4ccb43e
654	1	9HYDOJpZiPDUx++jShuNXUMdUK/+lx3Zxj9UX89Fxkk=	120fdbc6-ff5d-4b70-8f44-ba5b8c8d2e56	2026-05-14 12:11:14.893695	2026-06-13 12:11:14.893728	2026-05-14 12:27:40.45185	2026-05-15 11:04:46.467927	655	57e9123b-059a-49b9-beb3-4e39e861086a
655	1	UQcCEF2sRzUSa6Apje+I7J6dzBjGvtO8c5cUE6FV9Hs=	6d014892-fb74-4701-8029-f28596a73e12	2026-05-14 12:27:40.452361	2026-06-13 12:27:40.452457	2026-05-14 13:09:32.941931	2026-05-15 11:04:46.467927	656	57e9123b-059a-49b9-beb3-4e39e861086a
657	1	koBwTna7gFtMRECddxwm9myfc79hOpStye+T4kUqU2A=	dd054852-4b6d-4bc8-9436-239126aa1071	2026-05-14 14:54:15.439108	2026-06-13 14:54:15.439238	\N	2026-05-15 11:04:46.467927	\N	57e9123b-059a-49b9-beb3-4e39e861086a
656	1	bdZS9AQ/s9D/VCc20kFtff7tdgI5xTzwCGSbFVB/M1A=	00356d6d-84e7-41df-8e31-69a5a7a69c3b	2026-05-14 13:09:32.942535	2026-06-13 13:09:32.942618	2026-05-14 14:54:15.438426	2026-05-15 11:04:46.467927	657	57e9123b-059a-49b9-beb3-4e39e861086a
662	1	jTL4b/4nSUtxM2Op3SSI5dVQcZV3rYpI6u8z4e+fE2E=	c62aa397-0f23-4429-833d-66a787792571	2026-05-15 11:04:46.729093	2026-06-14 11:04:46.729163	2026-05-15 11:52:11.791042	\N	663	c12d52e8-953d-40a7-a146-abb14b4bdc9b
663	1	21FpocO9lC/yVWB4hUAclp7qTg6LyB3SswZ4pgrnFKk=	bb967aa1-3684-4178-a5ff-c78f2a7a1cd9	2026-05-15 11:52:11.791544	2026-06-14 11:52:11.791602	2026-05-15 13:25:50.96545	\N	664	c12d52e8-953d-40a7-a146-abb14b4bdc9b
664	1	TF+ZZcYpY28MJnAWyMCRqu/XZP64ZdVbRkuMkDzMZyc=	482b3bf8-9193-4eb1-922c-6755253a4ce8	2026-05-15 13:25:50.965943	2026-06-14 13:25:50.966051	2026-05-15 15:43:19.460759	\N	665	c12d52e8-953d-40a7-a146-abb14b4bdc9b
665	1	jc9PZZNZJSvniKwYpmCAGimbj+rsZg6QvXPJXB+JGYo=	5286fd3c-9b7a-4bf0-b50d-5e3e2457a369	2026-05-15 15:43:19.461343	2026-06-14 15:43:19.461433	2026-05-15 16:30:06.034911	\N	666	c12d52e8-953d-40a7-a146-abb14b4bdc9b
666	1	95lor3a2+2h2TxPT8j3Hmi04HSLk2yWr9css7EvKVR4=	a6379ed5-42fc-4d43-ace7-d5e5e1e4c4c1	2026-05-15 16:30:06.035396	2026-06-14 16:30:06.035478	2026-05-15 16:48:02.781151	\N	667	c12d52e8-953d-40a7-a146-abb14b4bdc9b
667	1	OJ+0tbok9w63Y6b8OP2Ylb3VOqAgT8+HzH/ZdQbW6QA=	9bc05a73-bb36-4181-933b-1996b212f28d	2026-05-15 16:48:02.781575	2026-06-14 16:48:02.781631	2026-05-15 18:13:33.621121	\N	668	c12d52e8-953d-40a7-a146-abb14b4bdc9b
668	1	tk746qmCcbKFBGrusk9KiJu33O+85DGrUq/jpZSC+WU=	e938af55-4c97-4d78-badb-5adb00bc3337	2026-05-15 18:13:33.621707	2026-06-14 18:13:33.621757	2026-05-15 19:04:31.489131	\N	669	c12d52e8-953d-40a7-a146-abb14b4bdc9b
669	1	tfjPQwGsPJ81y0GaXcvoS0cPSX27tb6uai5MFfR1PEs=	83bc8d26-a981-4e6c-868f-3de864cc97ae	2026-05-15 19:04:31.489665	2026-06-14 19:04:31.489752	2026-05-15 19:26:43.609541	\N	670	c12d52e8-953d-40a7-a146-abb14b4bdc9b
670	1	HjU1vpgQmEXQd4mRT6TFV0rBlIyizJokVolWUXBMpH4=	4d01800f-95d9-4a71-9653-6c6cda236ce7	2026-05-15 19:26:43.610024	2026-06-14 19:26:43.610163	2026-05-15 20:09:23.571047	\N	671	c12d52e8-953d-40a7-a146-abb14b4bdc9b
671	1	ku5f+nF2r/CzpX3JtYcCmwFUzSnvJo5msP83e6Lo02c=	73d871f0-adfe-48f2-8a64-a67a322f9e27	2026-05-15 20:09:23.571553	2026-06-14 20:09:23.571643	2026-05-15 20:28:09.156436	\N	672	c12d52e8-953d-40a7-a146-abb14b4bdc9b
672	1	DX2NO80VdEtHd0Fqs39RDU9f++I7pqqOX9FLIHMFwaM=	1a943161-1ba2-4090-9d6e-4500724093c2	2026-05-15 20:28:09.15687	2026-06-14 20:28:09.157	2026-05-16 08:56:54.654245	\N	673	c12d52e8-953d-40a7-a146-abb14b4bdc9b
673	1	OVPkxOQ6pkR2f+Z/sp3U2XrF3WUxWy2Lw78vb6BT5/A=	47cb3b09-229b-4f80-83d0-0c7dcc12a243	2026-05-16 08:56:54.654691	2026-06-15 08:56:54.654748	2026-05-16 09:12:30.616035	\N	674	c12d52e8-953d-40a7-a146-abb14b4bdc9b
674	1	nPtlAP9BXwJCLluJjm/MRxEUsgPGxOFqaDYpoweqpLk=	ec563225-2c10-4931-b098-6d084268bad9	2026-05-16 09:12:30.61648	2026-06-15 09:12:30.616542	2026-05-16 09:30:17.773992	\N	675	c12d52e8-953d-40a7-a146-abb14b4bdc9b
675	1	r4gNFOsjfuiJcaDTjJa7huKsdrhhREQ471pcTdVjMBs=	da44161c-061a-4a35-a9f6-eedb26f2bf9d	2026-05-16 09:30:17.774593	2026-06-15 09:30:17.774689	2026-05-16 09:44:59.13689	\N	676	c12d52e8-953d-40a7-a146-abb14b4bdc9b
676	1	nWMIe52M+l1ciivHX0W3EDUb54nW6H23HTwdkiZFBn0=	73766e4d-0f18-403b-9880-37d3194b2ffb	2026-05-16 09:44:59.137307	2026-06-15 09:44:59.137392	2026-05-16 10:08:37.068036	\N	677	c12d52e8-953d-40a7-a146-abb14b4bdc9b
677	1	lKNFCN8sX9C/h+5thNOYDw4KmV0mZOP6jKCAj8kb9m0=	7d30b0ac-b2a8-4dfe-9435-0b82dd5fb97a	2026-05-16 10:08:37.06845	2026-06-15 10:08:37.068491	2026-05-16 11:25:21.249153	\N	678	c12d52e8-953d-40a7-a146-abb14b4bdc9b
678	1	GPZh67yQow6afbrnlp2osI7kPqk51Bk3vz6cG5DS+vM=	b40a8a31-1895-4ec6-a07e-59a78021ea28	2026-05-16 11:25:21.249565	2026-06-15 11:25:21.249611	2026-05-16 11:51:34.445855	\N	679	c12d52e8-953d-40a7-a146-abb14b4bdc9b
679	1	mkFRW54sSF9rhHDHLwYsULiVt6lT4feFZ22GIjFlTmg=	bd6c8d98-d178-495a-9878-1ec763fb1354	2026-05-16 11:51:34.446385	2026-06-15 11:51:34.446462	2026-05-16 12:33:31.0091	\N	680	c12d52e8-953d-40a7-a146-abb14b4bdc9b
680	1	G3bqLjZSf0X+eEdpb05fnuKToZ8x/ScmPt8wNTGUZWs=	0492857c-9a2f-4748-8050-891629a56c02	2026-05-16 12:33:31.00968	2026-06-15 12:33:31.00976	2026-05-16 14:44:14.697813	\N	681	c12d52e8-953d-40a7-a146-abb14b4bdc9b
681	1	0ADJueMxdMP1EejNhLdMDksMS6Ql95VzupFMlklHcts=	13466d67-229a-4da4-90b9-ccd8dc72cb6d	2026-05-16 14:44:14.698368	2026-06-15 14:44:14.69843	2026-05-16 15:12:07.535847	\N	682	c12d52e8-953d-40a7-a146-abb14b4bdc9b
682	1	4t3+z2BZqe9ZarJH+JvxvANodj1c+HKeRrX/QZ+nUgQ=	bd8591fd-e9c7-4813-9447-2a1337808b66	2026-05-16 15:12:07.536495	2026-06-15 15:12:07.536618	2026-05-17 20:34:35.871682	\N	683	c12d52e8-953d-40a7-a146-abb14b4bdc9b
683	1	Zt9PLnI91rVAsEqk7bWtBFS5hxVw+YUirMk2dg2i5gU=	e6392fa5-6eab-4beb-a919-48bc86b55d28	2026-05-17 20:34:35.872285	2026-06-16 20:34:35.872351	2026-05-18 04:00:31.274397	\N	684	c12d52e8-953d-40a7-a146-abb14b4bdc9b
684	1	8edmhB3Co4ExUqM59G1T0Z0DIw/+Fl86Z4++ma0iO/E=	92905f36-83c6-407b-9dd9-fba5dd83a7a4	2026-05-18 04:00:31.274988	2026-06-17 04:00:31.275078	2026-05-18 07:06:06.410238	\N	685	c12d52e8-953d-40a7-a146-abb14b4bdc9b
685	1	mDXJulLSgx+VtdvIlMHPBOErclGuno5jjWUxOjeI+o0=	98f6dc4b-db73-4412-811e-5ad99b3bb89c	2026-05-18 07:06:06.410762	2026-06-17 07:06:06.410853	2026-05-18 10:01:09.467634	\N	686	c12d52e8-953d-40a7-a146-abb14b4bdc9b
659	1	jS6XpRIDc24PJ9/JsAjTHkUjiyWBs0yAxgW0DGsq9R8=	4e499241-9d39-4c0d-b42d-09b38cbc9970	2026-05-14 19:22:52.136304	2026-06-13 19:22:52.136623	\N	2026-05-24 22:03:28.399945	\N	7b662d27-4e36-4d7a-b5c7-e53de06716cf
686	1	6cuhcYOkNBgg8HxeqjXRsfNP3Bn76s7dwWlDPxWq+yc=	a097f24b-3bcf-4b33-bcbf-01c55319c68e	2026-05-18 10:01:09.468146	2026-06-17 10:01:09.468244	2026-05-18 11:20:55.343245	\N	687	c12d52e8-953d-40a7-a146-abb14b4bdc9b
658	1	3g0OGiCEiA8UleO0GLaEAZS2kfEtVp9KXQU7ua44u18=	a2e530b8-dd3c-4aaa-9785-96950d6e0ebe	2026-05-14 17:21:31.626958	2026-06-13 17:21:31.627015	\N	2026-05-18 16:43:51.925445	\N	9ee93a6e-a3eb-4bbc-8915-7500fdbf9b41
688	1	0/SwNxRWvQSLpyoctibfz8pZvloiF1elOeDnOINpUNM=	dafaedfb-8371-4e37-b226-116f9073504c	2026-05-18 16:43:52.243431	2026-06-17 16:43:52.243557	2026-05-18 16:58:52.695167	\N	689	9209c524-fa40-4628-82cb-73422f2d2c83
689	1	tjHn02jjk5M+jqNdzpz2j2OlKbswcM/ItnfeW07SROI=	65c2e7d5-e36a-4fd4-b5a3-2d4b133ee2d0	2026-05-18 16:58:52.695947	2026-06-17 16:58:52.696095	2026-05-18 17:23:38.811248	\N	690	9209c524-fa40-4628-82cb-73422f2d2c83
690	1	8YH4MXi4sC/AXEhBhUkqstFuwI8LRZe8WoIWaU222A4=	604a6593-f6e0-42f3-b61c-083b66084573	2026-05-18 17:23:38.811815	2026-06-17 17:23:38.811945	2026-05-18 17:39:13.895479	\N	691	9209c524-fa40-4628-82cb-73422f2d2c83
691	1	MjqG3z+5+sohjTDFQmBrV6iPoIBsw4nZjkcZL5FFMK8=	232041ec-7fe0-4bc3-924a-0c136993fe8c	2026-05-18 17:39:13.896033	2026-06-17 17:39:13.896129	2026-05-18 17:58:34.249163	\N	692	9209c524-fa40-4628-82cb-73422f2d2c83
692	1	cdkGj3NLQtlKiUPSuWedcrVg7rD5WTnv9kr9xlIajNg=	bd470d2b-9ba0-48ce-9486-f6e0bd21d9d9	2026-05-18 17:58:34.249662	2026-06-17 17:58:34.249732	2026-05-20 12:40:34.439437	\N	693	9209c524-fa40-4628-82cb-73422f2d2c83
693	1	yGL50JOc9UJnJt3/hb4hbGdpydDDngMZXtQH495nJho=	3bb1d99b-3159-44fc-aad5-473d938cbaac	2026-05-20 12:40:34.439948	2026-06-19 12:40:34.440004	2026-05-20 13:44:14.158125	\N	694	9209c524-fa40-4628-82cb-73422f2d2c83
694	1	qyWQSox4qDovSUQ0Xemuetfmq5PEzqjF/R2crKDlFCc=	fe0fff15-7db5-432c-9d4b-0e669c05ecb6	2026-05-20 13:44:14.158451	2026-06-19 13:44:14.158486	2026-05-20 13:59:15.623561	\N	695	9209c524-fa40-4628-82cb-73422f2d2c83
695	1	GYXGH4OBME6vIbXvfkXcfKCzejYcl5/tSh/jpnNt7Zo=	ca18cdae-5b81-41e9-aa84-8abb459a1451	2026-05-20 13:59:15.624004	2026-06-19 13:59:15.624072	2026-05-20 14:34:07.88395	\N	696	9209c524-fa40-4628-82cb-73422f2d2c83
696	1	WbakiRdUZxUtbG3uDTUi2JDKpCyeqBbsrzZjxzFKpJU=	7e41857c-f203-4cff-b1cc-1a9762d87b19	2026-05-20 14:34:07.8844	2026-06-19 14:34:07.884465	2026-05-22 23:26:56.654552	\N	697	9209c524-fa40-4628-82cb-73422f2d2c83
697	1	qHAkBNMbi7T/Z/UUMGEBP4nwyQmHUQnNOk2ebGDh6AQ=	a4432ce1-bc2f-4c0b-9252-bb85e5a1ec0d	2026-05-22 23:26:56.654873	2026-06-21 23:26:56.654973	2026-05-22 23:42:32.645019	\N	698	9209c524-fa40-4628-82cb-73422f2d2c83
698	1	7big6sHbvBdFHNJPaAePGrF8N6POBhYTw5GEbqB3Bho=	48068ba2-6e18-44e3-b288-2c4ea0f215c2	2026-05-22 23:42:32.645483	2026-06-21 23:42:32.645586	2026-05-23 07:54:59.821355	\N	699	9209c524-fa40-4628-82cb-73422f2d2c83
699	1	jyQ7bJHKzicz+cyE7ly4z2ZDwExpE0BUSPDpNrhjVgk=	9dd11772-277d-41ef-977d-2bc4af41d6bf	2026-05-23 07:54:59.821778	2026-06-22 07:54:59.821847	2026-05-23 08:17:40.344866	\N	700	9209c524-fa40-4628-82cb-73422f2d2c83
700	1	vOrCOakVljY3FWhFLJvZEi9kHYXI5fRGVoG6pu8Sfro=	337a5fae-8ba2-4fb2-91fc-38d1f16b3781	2026-05-23 08:17:40.345277	2026-06-22 08:17:40.345342	2026-05-23 12:47:01.445656	\N	701	9209c524-fa40-4628-82cb-73422f2d2c83
701	1	vV+LYWMhQ8XubOyq6VTkTe9EblWn4NaOsGu7xVFT8L4=	f1e46efd-8dd6-445d-bbf7-f0be2a07da6b	2026-05-23 12:47:01.446113	2026-06-22 12:47:01.446185	2026-05-23 13:05:50.992853	\N	702	9209c524-fa40-4628-82cb-73422f2d2c83
702	1	3zP7UjZcnEio57K6t5PF9q05UWB1L+Mpr2afDH3YMic=	c8a062d4-78b7-453f-b855-f6c05e2b956b	2026-05-23 13:05:50.993228	2026-06-22 13:05:50.993289	2026-05-23 17:27:26.236329	\N	703	9209c524-fa40-4628-82cb-73422f2d2c83
703	1	H22uxDqidiep+OxYqKTn0qidH8k/ELTEEggruIjMN/I=	52f07219-a626-4eb7-873f-8f752b878625	2026-05-23 17:27:26.237097	2026-06-22 17:27:26.237227	2026-05-23 19:47:50.851656	\N	704	9209c524-fa40-4628-82cb-73422f2d2c83
704	1	MzRLCDEiHaRAHxDcnggdXzfvCY92pVT1k/50E/d+o6A=	b6a059f6-33b2-44dc-b9af-db95c1a7b2b5	2026-05-23 19:47:50.852216	2026-06-22 19:47:50.852355	2026-05-24 08:09:26.645625	\N	705	9209c524-fa40-4628-82cb-73422f2d2c83
705	1	wyYNmvMect8VXeVwk6V4N8z3mhYnfNIiv2b1trc53gQ=	b0cdd9ed-5c45-4b2b-8e37-5a6a5c07584c	2026-05-24 08:09:26.645986	2026-06-23 08:09:26.646048	2026-05-24 08:24:25.339534	\N	706	9209c524-fa40-4628-82cb-73422f2d2c83
706	1	Alv1hjPg9EGNFukb/Ls5csB3Lx/KvyI3AAAlyYeZ4bo=	bebaf6e3-420d-45d6-a765-6658164952ef	2026-05-24 08:24:25.341184	2026-06-23 08:24:25.341509	2026-05-24 08:41:29.104006	\N	707	9209c524-fa40-4628-82cb-73422f2d2c83
707	1	c0j8t1M9qE93OEURRRWGOWyICLyjFnJm9oDkhSoQVoo=	25de481e-8a74-4411-abbb-18be6f2d737f	2026-05-24 08:41:29.104916	2026-06-23 08:41:29.105165	2026-05-24 08:56:48.305473	\N	708	9209c524-fa40-4628-82cb-73422f2d2c83
708	1	O8JCXfz1hlx8OGqv9eF9hwbDSUVPfT5AbWndwVBSsoc=	5d88aff4-d498-481f-b17f-69511bfe09e7	2026-05-24 08:56:48.306104	2026-06-23 08:56:48.306183	2026-05-24 10:06:34.04504	\N	709	9209c524-fa40-4628-82cb-73422f2d2c83
709	1	OnHGK3agbEfO1F9552bVc3oHc9ShLmOw/6v4iCj1tDI=	1f8e0884-8ed9-4a46-b162-4c45f23c6ed2	2026-05-24 10:06:34.045077	2026-06-23 10:06:34.045077	2026-05-24 14:53:30.407971	\N	710	9209c524-fa40-4628-82cb-73422f2d2c83
710	1	p8GtINSpPqNb9X2HOiVc9Dk+FmGWdHzRoAH2ZMZbsFE=	6d3bf01e-e6a2-4ed0-b9d5-dc6b9c67a4fc	2026-05-24 14:53:30.408578	2026-06-23 14:53:30.408637	2026-05-24 15:23:54.255267	\N	711	9209c524-fa40-4628-82cb-73422f2d2c83
711	1	IDXwKlCapEp+61t3XH7Z/SRDvIFNuwC9OC/5f2qydLU=	37e11dbf-9dbd-47ee-b47a-150d51196d28	2026-05-24 15:23:54.256282	2026-06-23 15:23:54.256374	2026-05-24 18:13:54.877501	\N	712	9209c524-fa40-4628-82cb-73422f2d2c83
712	1	YDnb113gURFBlb9k3cMn3+C/ddSlrN+ZMzLBwdmMq1A=	6f786dce-1239-41d2-aed7-e0928d471ede	2026-05-24 18:13:54.878221	2026-06-23 18:13:54.878381	2026-05-24 18:47:41.344472	\N	713	9209c524-fa40-4628-82cb-73422f2d2c83
713	1	MWOYTzIxosX6Uvma3Jf5sabDQ/hMi4orrgRySQnQSfA=	8b346f80-c348-41a1-b063-778aa201d0d1	2026-05-24 18:47:41.344859	2026-06-23 18:47:41.344913	2026-05-24 19:14:47.938666	\N	714	9209c524-fa40-4628-82cb-73422f2d2c83
714	1	StPVQe2Y4740VnZB+Yq7B34gR988gtac7S9RmxAmJSI=	f4352b7d-6dd9-4977-8f86-8d9dcc45eb19	2026-05-24 19:14:47.939233	2026-06-23 19:14:47.939309	2026-05-24 19:40:54.773435	\N	715	9209c524-fa40-4628-82cb-73422f2d2c83
715	1	Oax/NRA4eawdM4eK19GBWMTyu0JNDIgvufiY3w43IEo=	262045e9-437c-48b3-b5e9-2bca8751643a	2026-05-24 19:40:54.774229	2026-06-23 19:40:54.774318	2026-05-24 19:56:04.328127	\N	716	9209c524-fa40-4628-82cb-73422f2d2c83
716	1	53L+vf/5Hpe28dh0vB6fJNqJ/p0eSP9XfbHd42u7Ur0=	d288e047-9a3c-4e2b-b53c-55b3af7eff1d	2026-05-24 19:56:04.328688	2026-06-23 19:56:04.328759	2026-05-24 20:11:12.401879	\N	717	9209c524-fa40-4628-82cb-73422f2d2c83
717	1	l5c2XJWuDjOFbAuMRR576+8k99zgayIR9Trr3ExYx5M=	3dd807b0-b217-4fff-8945-2730bfccd538	2026-05-24 20:11:12.402409	2026-06-23 20:11:12.402476	2026-05-24 20:26:24.120869	\N	718	9209c524-fa40-4628-82cb-73422f2d2c83
718	1	BqYyF3cNSvm+soDrujAA/LvkL2L5nt6v851Sy6Us4WE=	a55f0ad2-b649-4173-8744-a4b82baed9c6	2026-05-24 20:26:24.121757	2026-06-23 20:26:24.121874	2026-05-24 20:41:24.047068	\N	719	9209c524-fa40-4628-82cb-73422f2d2c83
660	1	1RN7NEamnjL0oFxNKcUkKLz1QPLsO3aB/w5Xeopj/qc=	a74d0deb-7688-4d0f-877f-db6123304e11	2026-05-15 05:47:46.929634	2026-06-14 05:47:46.92973	\N	2026-05-24 22:03:28.399945	\N	09e1b076-6a99-403a-89e2-8ed2e99149b9
661	1	kLhgaaLNyWbNJccXNPSAR53i6GQdyzTW/y7G8gDc8X4=	d67547ad-470e-402d-a04b-be6b066524e0	2026-05-15 06:03:04.09154	2026-06-14 06:03:04.091613	\N	2026-05-24 22:03:28.399945	\N	12cf9d01-dead-43d4-9db0-68f724e389f2
687	1	AX6kFBDAeCeWcytzrF/T3DbDnObDsAe2uNOkrK3WOeY=	92c2c4c3-712f-46df-af47-e38de2399702	2026-05-18 11:20:55.343706	2026-06-17 11:20:55.343792	\N	2026-05-24 22:03:28.399945	\N	c12d52e8-953d-40a7-a146-abb14b4bdc9b
719	1	hx7y0RjyVEfOhMR0otZ2xfTrmMPX/MHoZte9oKpVahU=	19a44a6c-b8c2-4f95-a746-dd8860bfa7a1	2026-05-24 20:41:24.047495	2026-06-23 20:41:24.047561	\N	2026-05-24 22:03:28.399945	\N	9209c524-fa40-4628-82cb-73422f2d2c83
\.


--
-- Data for Name: system_settings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.system_settings (key, value) FROM stdin;
heads_chat_id	74
\.


--
-- Data for Name: user_settings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.user_settings (user_id, theme, notifications_enabled) FROM stdin;
17	\N	t
21	light	t
11	\N	f
3	light	t
19	light	t
7	light	t
1	light	t
23	\N	t
14	light	t
\.


--
-- Data for Name: users; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.users (id, username, name, password_hash, created_at, last_online, department_id, avatar, midname, surname, is_banned, status_type, status_expires_at) FROM stdin;
14	sofia	София	$2a$12$j63.VL70dg8hIyrVBLlrzOzB0lTZo.zdblJ7KxagpbZV1/BPPko/S	2025-11-01 10:55:29.999365	2026-03-15 16:04:14.61856	4	/avatars/users/d829485e-e8e5-4804212-321995132ds.webp	\N	Морозова	f	online	\N
11	irina	Ирина	$2a$12$hJUbFk3U2HAsSxA5eOYh4OYbBtmEKEf3d1ee9P9sURHU1TqLbzOXq	2025-11-01 10:55:29.999365	\N	3	/avatars/users/d815e-e8e5-480433-3ee3-21995bb132.webp	Алексеевна	Сергеева	f	online	\N
3	gfdgfd	fdssfsdf	$2a$11$HQxg8B0ivASe/dXy2xR3NO.g2uB.p.kcLKqbJMVT0OyuHMbDM1vPa	2025-12-24 10:02:40.321516	\N	6	\N	fdsfdsf	Олег	f	online	\N
8	anna	Анна	$2a$12$HcAv0lA.zq.pkDmGgHUl6O6GlEvNi1JIBbScxRJUeKYhaHITAvg/C	2025-11-01 10:55:29.999365	\N	2	/avatars/users/d829485e-e8e5-4804212-3ee3-21995bb132ds.webp	Сергеевна	Козлова	f	online	\N
9	dmitry	Дмитрий	$2a$12$HStYP3mjOl.IhjGeJPT64uY9f1m.cLYCzqI27ay3JcZmkGsl.TXbG	2025-11-01 10:55:29.999365	\N	1	/avatars/users/d82943341e-e8e5-480433-3ee32213-2111995bb132.webp	Андреевич	Смирнов	f	online	\N
13	pavel	Павел	$2a$12$8WHGSilJHCB5pncp5MKmfuPjsfKlyNNgkxGtSyJhPcQmlFw40B1Ye	2025-11-01 10:55:29.999365	\N	1	/avatars/users/d82943341e-e8e5-480433-3ee32213-21995bb132ds.webp	Дмитриевич	Соколов	f	online	\N
15	artem	Артем	$2a$12$a4FodrD0xTSzBsE1TcfEwOIVPACYvZomU3dX5eM8AW.1dZPv17QCa	2025-11-01 10:55:29.999365	\N	5	/avatars/users/d829431e-e8e5-480433-3ee32213-21995bb132ds.webp	Викторович	Новиков	f	online	\N
10	alex	Алексей	$2a$12$kZnjpzDPDYHhwtmQWUgFwOm20GQLXV8EMguw2w.Yk5C5eh.HH1NR.	2025-11-01 10:55:29.999365	2026-03-03 16:32:18.380355	1	\N	Михайлович	Иванов	f	online	\N
16	maria	Мария	$2a$12$Ni9zocPHcCaXVbOIHsiRvuly9GFgi.HNjKVQ0x51cy/hYpfFIbKme	2025-11-01 10:55:29.999365	\N	4	/avatars/users/d829485e-e8e5-480412212-321995132ds.webp	Александровна	Федорова	f	online	\N
17	andrey	Андрей	$2a$12$BDCRzXue5pND.guTZoS69utm2NWWlhYF5uxGyetbAoSrIHdeDsiQW	2025-11-01 10:55:29.999365	\N	6	/avatars/users/d8485e8e5-480133-3ee3-21995bb132ds.webp	Евгеньевич	Попов	f	online	\N
18	sergey	Сергей	$2a$12$U/UUxF3EpDXi13s/SiJSaeTUqK.UUs3MVqJalTn6nxWV4fY/Lxkpq	2025-11-01 10:55:29.999365	\N	6	/avatars/users/d8485e8e5-480433-3ee3-21995bb132ds.webp	Владимирович	Лебедев	f	online	\N
20	roman	Роман	$2a$12$0jL6dFnBRApZgZx3hZNUEueVkIwBUIW4bC4goE7giRmT8jRupI1KG	2025-11-01 10:55:29.999365	\N	2	/avatars/users/d82943341e-e8e5-480433-3ee32213-2111995bb132ds.webp	Станиславович	Егоров	f	online	\N
23	tanya	Татьяна	$2a$12$boAs0d4jKHubPiHuYzyu6Onc0QRav.4bKY5kNzjEO.mQ.uSgGpW2S	2025-11-01 10:55:29.999365	\N	5	/avatars/users/d829485e-e8e5-4804-3ee3-21995bb132ds.webp	Ивановна	Голубева	f	online	\N
24	denis	Денис	$2a$12$0AiwFo1W98.fTQmhimcvRenOtkBBHhqxrFclyBXWUi6mdMGbIrKQK	2025-11-01 10:55:29.999365	\N	2	/avatars/users/d829485e-e8e5-4804-9ee3-21995bb150cb.webp	Алексеевич	Степанов	f	online	\N
25	yana	Яна	$2a$12$UUkLsRoIJ/Deufh.LDrumeBV.UEF7oEtST1lsPWTReHm7HvJmvKb.	2025-11-01 10:55:29.999365	2026-04-08 11:35:19.664442	4	/avatars/users/d829485e-e8e5-4804-3ee3-21995bb150ds.webp	Романовна	Васильева	f	online	\N
19	katerina	Екатерина	$2a$12$He.REUapl8p.wSYWFaer5OHHbdaEf9D49c2pwOARvxRPqFPUyMxIG	2025-11-01 10:55:29.999365	2026-02-11 20:48:27.851832	1	/avatars/users/d829485e-e8e5-4804212-321995bb132ds.webp	Олеговна	Кузнецова	f	online	\N
22	vladimir	Владимир	$2a$12$JnC8acx8dQzqiudoK.bihefj9kWdnBWYUv6vOY95ulOY7xIXGff2.	2025-11-01 10:55:29.999365	\N	6	\N	Борисович	Семенов	f	online	\N
21	olga	Ольга	$2a$12$.r3YYCLYrcjQ2B5D8PPFWemkqZ3sHmL./zXE0dqrQuqKcflrrhAaG	2025-11-01 10:55:29.999365	2026-04-22 19:28:07.700563	13	/avatars/users/d8485e-e8e5-480433-3ee3-21995bb132ds.webp	Ивановна	Павлова	f	online	\N
1	admin	Александр	$2a$11$DxNLIDpx4g8YGUiO2TxOxOpz57vbAjczlZwwUAOgAB0ae14GPzPAy	2025-08-12 16:26:35.259187	2026-05-25 07:43:34.637258	1	/uploads/avatars/users/5d28d554-9e98-4363-b4ee-d4c5348b7cbb.webp	\N	Шихов	f	busy	\N
12	nikita	Никита	$2a$12$Um55J8R.n5Ouhk.vae7kGO9lLGQvXwTPTnMFEUfQh1neUp9cs2Mpe	2025-11-01 10:55:29.999365	\N	1	/avatars/users/d829485e-e8e5-480433-3ee32213-21995bb132ds.webp	Павлович	Волков	f	online	\N
7	oleg	Олег	$2a$12$eSfFtHh6tozgFZwi1cHDquiX6v6vE58qLTu1tv0xKOSar7Lb6Zwl.	2025-11-01 10:55:29.999365	2026-05-14 12:11:04.300863	13	/avatars/users/d815e-e8e5-480433-3ee3-21995bb132ds.webp	Иванович	Петров	f	do_not_disturb	\N
\.


--
-- Data for Name: voice_messages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.voice_messages (message_id, duration_seconds, file_path, file_size, waveform) FROM stdin;
497	2.8433172	uploads/chats/6/bcb267b9-1edc-4c41-8bc8-9eeab799377b.wav	89646	\N
499	4.075937	uploads/chats/6/55e45301-ccc5-44b7-aeaf-58eef929c6c5.wav	128046	\N
517	11.8801162	/uploads/chats/66/d1b9cc67-0b7a-464c-9b9d-059c5ac38bbe.wav	377646	\N
498	7.6794119	uploads/chats/6/9dd32d30-fcac-45f2-acaf-d7dd4fb613b7.wav	243246	\N
501	2.5784837	/uploads/chats/6/dde3dfcc-9a82-4414-b4ba-f3b0d39412e4.wav	80046	\N
653	1.3809009	/uploads/chats/66/dd06d085-e932-4689-99e8-cbdc53ce822b.wav	42028	\N
972	5.7328483	/uploads/chats/20/c58719a7-722f-4343-8150-f582b25557f8.wav	182316	AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==
989	11.3747895	/uploads/chats/6/4fce467d-f191-4767-a780-0189d50aac09.wav	362540	CQIFBAICAwQCAgMDAwICAgMCAgIBAgICAgICAgIBAgECAgECAQIBAb/rckoXAgECCAICAgICAgIDAgECAgIIBQMDAwICAQEBBAICGQ0JBwICAgICAgMCAgICAgQHAgMCAwICAg==
990	9.1260786	/uploads/chats/6/ee08abd0-2498-44b7-9a9e-e9bcd25a9301.wav	290860	AQoCAwICAgICAgQEAgMDCAUFBAMFAwUFBAcEBAMFBQYGBggGBgYHCAgIDAkJCwoKCAcIBggJCgoLCQoICAkJCQgHBwYGBgYHCAcFBQYFBQQEAwUDBAMDAwMDAwMDAgICAgIDCA==
991	9.9799961	/uploads/chats/6/75bd2473-8c4c-41f0-866a-bd31dc02423a.wav	318508	CAIDAwECAQEBAQEBAgEBAQIBAQECAQEHBAIBAgQEAgIBCAUDAQEHBgIBAgIFAQEBAggDAQEBCgMCAQEFCAIBAgEDAwgCBQgCAgQCAQIBAQIRBwQCDwMBAQEBAwIBAgICAQIBAQ==
992	3.2224561	/uploads/chats/6/dfd35066-8174-40f6-8bfe-794ad582395a.wav	102444	AgcBAQEBAQEBAQEBAQEiEgcEAgIBAQEBAQQFAwICAgEBAQEBAQoJBgICAwMDBQMCAgMDBAMCBwUDBgMCCwcCBAMCCAUCBwQDBwsEBQcEAxIIAwUDAgYEAgIBBQYDAwMCBQoFBQ==
994	9.6216869	/uploads/chats/6/20445776-c2ba-46a2-8ff7-0503901978c3.wav	307244	AQAAAAABAgAAAAGKjyFLCv8jVg4XBf8gTxkGBde1YGIxCgaaNG0ZRgr/P3MXPQj/nDAoBwUCAQQBAgMBAwEBAQIB/+gQCQsJ+JxdPA0BAAAAAAAAAQIBBgUDBgICAQESBgEAAQ==
1002	4.3382998	/uploads/chats/6/d721ced3-355b-4ea7-9ed5-e56222844263.wav	137260	fGhsbG5ubG9sam5wa2lva2VtZGhsanBrbWtraGdlZGRpYGlra2drZ21uamZqa2ZmZGhpaXJsbG92dW9vb3BgaWlpZmtqam5tbXFvb3FwaWplZ21vbW5saW5sa21pampnZ2h7fA==
1003	15.6471881	/uploads/chats/6/9154ce55-2af7-4147-bcda-93d2c768cc3d.wav	499756	AAABAQD/m+dg/8b/t/fovqO9pJahugb/R/9//3v/cf//tf/bYi4HAwEAAQEBAAEAAAAEAgcDAgEBAAMAAAAAAC82BAYBAAABBA8GAQEDAQAAAAAAAAAAAAAAAAAAAAAAAAAAAQ==
1004	17.460188	/uploads/chats/6/d7732936-1f51-4e9d-9411-56923e8843e2.wav	558124	AQAAAAABAAABAQQDAAAAAAAAAAAAAAMCCAsGAwIBACETSR8OAwMCBwEAAQEAAAAAAAAAAAAAAAIAAQAAAgAAAo5QGHRThmsGAQEAAAAAAAIBAAACAAAA/+NgLAgDAgIBAAAAAQ==
1023	5.6633064	/uploads/chats/16/da055cfe-4cfc-40fa-8d31-2e7f5d466507.wav	180268	AAAAAAAAAAAAAAAAAAABAAAAAQEAAQAAAQAAAAAAAAAAAAAAAAABAAEAAAEBAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAUA4HBsXEQQCAQICAg==
1024	2.2627147	/uploads/chats/16/738c8bb6-25a9-4ffc-ae93-164cd881461c.wav	71724	AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==
1032	5.7585637	/uploads/chats/16/adbf92bf-cec7-4f8f-9be2-741670fbb9d6.wav	183340	AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQABAQAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQ==
1033	4.5503545	/uploads/chats/16/7272c1ea-c531-4606-8316-303fef6f7983.wav	144428	AAAAAAABAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAQMAAAABAwQCAAAAAgMCAAAAAgMCAQAAAAACAQAAAAACAgAAAAAAAAIBAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==
1034	6.8224876	/uploads/chats/16/3ec21206-f9e7-43af-bcc4-50691eac77a7.wav	218156	AAAAAAADAgEBAAEBAf/Cukj/oDn/sWb/GvT/pEr/2qydQA4GBgQHAwICAQECAgEBAgUGBAIBAQEBAgEBAQIRBwEBAgIDAgICAgPYsnsFAwkDBQICAgEEAQEBAgIHBAEEAQEBAQ==
1182	7.1697051	/uploads/chats/16/ebc02132-fae4-4247-b0a1-f9a363ffe953.wav	227372	AAAAAAAAAQICAAEAAQKFvgwUSzYxDyxOGzdHVC1LFRQDAQEBAQEAAQABAgECAQEBAAEBAAEBAQEIBgn/+//6cQUEBHv/124oBQEBAQEBAQEBAQABAAEBAAEBAAAAAAABAAAAAQ==
1256	9.9869369	/uploads/chats/74/5b7e434d-0e17-47c9-8ed2-23c878790884.wav	317484	EQcFByQrBjQ4RQdUAgMDAwMVITpBGRsbEw0BAQEDDw0qTToxMi4JPTgLCAkFBAMDAgECAQEBAQMCAQQDAgEBAQIHCQMBAgEBAQEFAQEBAQEBAQEBBw0CR2JYUTwrHxsaCAUEAg==
1266	5.777103	/uploads/chats/81/7d6d800a-f472-4a84-b824-bbdbfb2447c4.wav	183340	AAEBAQEBAQEBAQEBAQEBCP8jHdZaYqZg3ltDpzAwfytl0FFPZC0wdTruVCl0N1SD4CnSUXIzsSV6MA8DAgIC/3YWE+EyDv85Ev9VCv9UdcMYCgQDAwECAgQCAQECAQEBAQEBAQ==
1273	7.4020295	/uploads/chats/74/bf6ec570-8189-4720-9f1d-767049b3d279.wav	235564	AAAAAAAAAAABAAAAAAAAAAAAAQEBAAAAAQAAAAACAgAAAAAAAQIBAQEDAwEDAwEBAgQDAwQDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAA==
\.


--
-- Name: Chats_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Chats_Id_seq"', 92, true);


--
-- Name: Departments_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Departments_Id_seq"', 15, true);


--
-- Name: MessageFiles_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."MessageFiles_id_seq"', 99, true);


--
-- Name: Messages_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Messages_Id_seq"', 1365, true);


--
-- Name: PollOptions_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PollOptions_Id_seq"', 92, true);


--
-- Name: PollVotes_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."PollVotes_Id_seq"', 222, true);


--
-- Name: Polls_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Polls_Id_seq"', 33, true);


--
-- Name: RefreshTokens_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."RefreshTokens_Id_seq"', 719, true);


--
-- Name: Users_Id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public."Users_Id_seq"', 3, true);


--
-- Name: departments Departments_ChatId_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments
    ADD CONSTRAINT "Departments_ChatId_key" UNIQUE (chat_id);


--
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");


--
-- Name: poll_votes UQ_Poll_User_Option_Vote; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_votes
    ADD CONSTRAINT "UQ_Poll_User_Option_Vote" UNIQUE (poll_id, user_id, option_id);


--
-- Name: chat_members chat_members_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chat_members
    ADD CONSTRAINT chat_members_pkey PRIMARY KEY (chat_id, user_id);


--
-- Name: chats chats_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chats
    ADD CONSTRAINT chats_pkey PRIMARY KEY (id);


--
-- Name: departments departments_head_id_unique; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments
    ADD CONSTRAINT departments_head_id_unique UNIQUE (head_id);


--
-- Name: departments departments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments
    ADD CONSTRAINT departments_pkey PRIMARY KEY (id);


--
-- Name: message_files message_files_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.message_files
    ADD CONSTRAINT message_files_pkey PRIMARY KEY (id);


--
-- Name: messages messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT messages_pkey PRIMARY KEY (id);


--
-- Name: poll_options poll_options_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_options
    ADD CONSTRAINT poll_options_pkey PRIMARY KEY (id);


--
-- Name: poll_votes poll_votes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_votes
    ADD CONSTRAINT poll_votes_pkey PRIMARY KEY (id);


--
-- Name: polls polls_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.polls
    ADD CONSTRAINT polls_pkey PRIMARY KEY (id);


--
-- Name: refresh_tokens refresh_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id);


--
-- Name: system_settings system_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.system_settings
    ADD CONSTRAINT system_settings_pkey PRIMARY KEY (key);


--
-- Name: user_settings user_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_settings
    ADD CONSTRAINT user_settings_pkey PRIMARY KEY (user_id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: users users_username_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- Name: voice_messages voice_messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.voice_messages
    ADD CONSTRAINT voice_messages_pkey PRIMARY KEY (message_id);


--
-- Name: idx_chat_members_last_read_message_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_chat_members_last_read_message_id ON public.chat_members USING btree (last_read_message_id);


--
-- Name: idx_chat_members_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_chat_members_user_id ON public.chat_members USING btree (user_id);


--
-- Name: idx_departments_head_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_departments_head_id ON public.departments USING btree (head_id);


--
-- Name: idx_messages_chatid_createdat; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_messages_chatid_createdat ON public.messages USING btree (chat_id, created_at);


--
-- Name: idx_messages_chatid_pinnedat; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_messages_chatid_pinnedat ON public.messages USING btree (chat_id, pinned_at) WHERE (pinned_at IS NOT NULL);


--
-- Name: idx_messages_forwarded_from_message_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_messages_forwarded_from_message_id ON public.messages USING btree (forwarded_from_message_id);


--
-- Name: idx_messages_reply_to_message_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_messages_reply_to_message_id ON public.messages USING btree (reply_to_message_id);


--
-- Name: idx_messages_target_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_messages_target_user_id ON public.messages USING btree (target_user_id);


--
-- Name: idx_poll_votes_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_poll_votes_user_id ON public.poll_votes USING btree (user_id);


--
-- Name: idx_polls_message_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_polls_message_id ON public.polls USING btree (message_id);


--
-- Name: idx_refresh_tokens_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_expires_at ON public.refresh_tokens USING btree (expires_at);


--
-- Name: idx_refresh_tokens_family_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_family_id ON public.refresh_tokens USING btree (family_id);


--
-- Name: idx_refresh_tokens_replaced_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_replaced_by ON public.refresh_tokens USING btree (replaced_by_token_id);


--
-- Name: idx_refresh_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_token_hash ON public.refresh_tokens USING btree (token_hash);


--
-- Name: idx_refresh_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_refresh_tokens_user_id ON public.refresh_tokens USING btree (user_id);


--
-- Name: idx_users_department_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_department_id ON public.users USING btree (department_id);


--
-- Name: chat_members trg_check_contact_uniqueness; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_check_contact_uniqueness AFTER INSERT ON public.chat_members FOR EACH ROW EXECUTE FUNCTION public.check_contact_uniqueness();


--
-- Name: messages trg_update_chat_last_message_time; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_update_chat_last_message_time AFTER INSERT ON public.messages FOR EACH ROW EXECUTE FUNCTION public.update_chat_last_message_time();


--
-- Name: chat_members ChatMembers_ChatId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chat_members
    ADD CONSTRAINT "ChatMembers_ChatId_fkey" FOREIGN KEY (chat_id) REFERENCES public.chats(id) ON DELETE CASCADE;


--
-- Name: chat_members ChatMembers_LastReadMessageId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chat_members
    ADD CONSTRAINT "ChatMembers_LastReadMessageId_fkey" FOREIGN KEY (last_read_message_id) REFERENCES public.messages(id) ON DELETE SET NULL;


--
-- Name: chat_members ChatMembers_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chat_members
    ADD CONSTRAINT "ChatMembers_UserId_fkey" FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: chats Chats_CreatedById_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.chats
    ADD CONSTRAINT "Chats_CreatedById_fkey" FOREIGN KEY (created_by_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: departments Departments_ChatId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments
    ADD CONSTRAINT "Departments_ChatId_fkey" FOREIGN KEY (chat_id) REFERENCES public.chats(id) ON DELETE SET NULL;


--
-- Name: departments Departments_Head_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments
    ADD CONSTRAINT "Departments_Head_fkey" FOREIGN KEY (head_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- Name: departments Departments_Parent_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.departments
    ADD CONSTRAINT "Departments_Parent_fkey" FOREIGN KEY (parent_department_id) REFERENCES public.departments(id) ON DELETE SET NULL;


--
-- Name: message_files MessageFiles_MessageId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.message_files
    ADD CONSTRAINT "MessageFiles_MessageId_fkey" FOREIGN KEY (message_id) REFERENCES public.messages(id) ON DELETE CASCADE;


--
-- Name: messages Messages_ChatId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT "Messages_ChatId_fkey" FOREIGN KEY (chat_id) REFERENCES public.chats(id) ON DELETE CASCADE;


--
-- Name: messages Messages_ForwardedFromMessageId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT "Messages_ForwardedFromMessageId_fkey" FOREIGN KEY (forwarded_from_message_id) REFERENCES public.messages(id) ON DELETE SET NULL;


--
-- Name: messages Messages_PinnedByUserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT "Messages_PinnedByUserId_fkey" FOREIGN KEY (pinned_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- Name: messages Messages_ReplyToMessageId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT "Messages_ReplyToMessageId_fkey" FOREIGN KEY (reply_to_message_id) REFERENCES public.messages(id) ON DELETE SET NULL;


--
-- Name: messages Messages_SenderId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT "Messages_SenderId_fkey" FOREIGN KEY (sender_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: poll_options PollOptions_PollId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_options
    ADD CONSTRAINT "PollOptions_PollId_fkey" FOREIGN KEY (poll_id) REFERENCES public.polls(id) ON DELETE CASCADE;


--
-- Name: poll_votes PollVotes_OptionId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_votes
    ADD CONSTRAINT "PollVotes_OptionId_fkey" FOREIGN KEY (option_id) REFERENCES public.poll_options(id) ON DELETE CASCADE;


--
-- Name: poll_votes PollVotes_PollId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_votes
    ADD CONSTRAINT "PollVotes_PollId_fkey" FOREIGN KEY (poll_id) REFERENCES public.polls(id) ON DELETE CASCADE;


--
-- Name: poll_votes PollVotes_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.poll_votes
    ADD CONSTRAINT "PollVotes_UserId_fkey" FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: polls Polls_MessageId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.polls
    ADD CONSTRAINT "Polls_MessageId_fkey" FOREIGN KEY (message_id) REFERENCES public.messages(id) ON DELETE CASCADE;


--
-- Name: refresh_tokens RefreshTokens_ReplacedBy_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT "RefreshTokens_ReplacedBy_fkey" FOREIGN KEY (replaced_by_token_id) REFERENCES public.refresh_tokens(id) ON DELETE SET NULL;


--
-- Name: refresh_tokens RefreshTokens_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT "RefreshTokens_UserId_fkey" FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: user_settings UserSettings_UserId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_settings
    ADD CONSTRAINT "UserSettings_UserId_fkey" FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: users Users_DepartmentId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT "Users_DepartmentId_fkey" FOREIGN KEY (department_id) REFERENCES public.departments(id) ON DELETE SET NULL;


--
-- Name: voice_messages VoiceMessages_MessageId_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.voice_messages
    ADD CONSTRAINT "VoiceMessages_MessageId_fkey" FOREIGN KEY (message_id) REFERENCES public.messages(id) ON DELETE CASCADE;


--
-- Name: messages messages_target_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.messages
    ADD CONSTRAINT messages_target_user_id_fkey FOREIGN KEY (target_user_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- PostgreSQL database dump complete
--

